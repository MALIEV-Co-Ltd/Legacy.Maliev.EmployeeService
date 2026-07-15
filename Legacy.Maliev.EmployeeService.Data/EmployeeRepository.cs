using Legacy.Maliev.EmployeeService.Application.Interfaces;
using Legacy.Maliev.EmployeeService.Application.Models;
using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.EmployeeService.Data;

/// <summary>Projection-first PostgreSQL employee repository.</summary>
public sealed class EmployeeRepository(EmployeeDbContext dbContext, TimeProvider timeProvider) : IEmployeeRepository
{
    /// <inheritdoc />
    public Task<EmployeeResponse?> GetEmployeeAsync(int id, CancellationToken cancellationToken) =>
        Project(dbContext.Employees.AsNoTracking().Where(employee => employee.Id == id)).SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(
        EmployeeSortType? sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<Employee> query = dbContext.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            var numeric = int.TryParse(value, out var id);
            var pattern = $"%{value}%";
            query = query.Where(employee =>
                (numeric && employee.Id == id) ||
                EF.Functions.ILike(employee.FirstName, pattern) ||
                EF.Functions.ILike(employee.LastName, pattern) ||
                EF.Functions.ILike(employee.FullName, pattern) ||
                EF.Functions.ILike(employee.Email, pattern) ||
                (employee.PhoneNumber != null && EF.Functions.ILike(employee.PhoneNumber, pattern)));
        }

        query = sort switch
        {
            EmployeeSortType.EmployeeId_Descending => query.OrderByDescending(value => value.Id),
            EmployeeSortType.EmployeeEmail_Ascending => query.OrderBy(value => value.Email),
            EmployeeSortType.EmployeeEmail_Descending => query.OrderByDescending(value => value.Email),
            _ => query.OrderBy(value => value.Id),
        };

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
        {
            return null;
        }

        var items = await Project(query)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PaginatedResponse<EmployeeResponse>(items, pageIndex, (int)Math.Ceiling(total / (double)pageSize), total);
    }

    /// <inheritdoc />
    public async Task<Employee> CreateEmployeeAsync(UpsertEmployeeRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new Employee
        {
            RoleId = request.RoleId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.PhoneNumber,
            Email = request.Email.Trim(),
            DateOfBirth = request.DateOfBirth,
            HomeAddressId = request.HomeAddressId,
            CreatedDate = now,
            ModifiedDate = now,
        };
        dbContext.Employees.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEmployeeAsync(int id, UpsertEmployeeRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Employees.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.RoleId = request.RoleId;
        entity.FirstName = request.FirstName.Trim();
        entity.LastName = request.LastName.Trim();
        entity.PhoneNumber = request.PhoneNumber;
        entity.Email = request.Email.Trim();
        entity.DateOfBirth = request.DateOfBirth;
        entity.HomeAddressId = request.HomeAddressId;
        entity.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEmployeeAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Employees.Where(value => value.Id == id).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AddressResponse>> GetAddressesAsync(CancellationToken cancellationToken) =>
        await dbContext.Addresses.AsNoTracking().OrderBy(value => value.Id).Select(ToAddress()).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<AddressResponse?> GetAddressAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Addresses.AsNoTracking().Where(value => value.Id == id).Select(ToAddress()).SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Address> CreateAddressAsync(UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new Address
        {
            Building = request.Building,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode,
            CountryId = request.CountryId,
            CreatedDate = now,
            ModifiedDate = now,
        };
        dbContext.Addresses.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAddressAsync(int id, UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Addresses.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Building = request.Building;
        entity.AddressLine1 = request.AddressLine1;
        entity.AddressLine2 = request.AddressLine2;
        entity.City = request.City;
        entity.State = request.State;
        entity.PostalCode = request.PostalCode;
        entity.CountryId = request.CountryId;
        entity.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetEmployeeIdsForAddressAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Employees.AsNoTracking().Where(employee => employee.HomeAddressId == id).Select(employee => employee.Id).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteAddressAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Addresses.Where(value => value.Id == id).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken) =>
        await dbContext.Roles.AsNoTracking().OrderBy(value => value.Id).Select(ToRole()).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<RoleResponse?> GetRoleAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Roles.AsNoTracking().Where(value => value.Id == id).Select(ToRole()).SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Role> CreateRoleAsync(UpsertRoleRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new Role { Name = request.Name, Description = request.Description, CreatedDate = now, ModifiedDate = now };
        dbContext.Roles.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRoleAsync(int id, UpsertRoleRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Roles.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetEmployeeIdsForRoleAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Employees.AsNoTracking().Where(employee => employee.RoleId == id).Select(employee => employee.Id).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DeleteRoleAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Roles.Where(value => value.Id == id).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <inheritdoc />
    public Task<SignatureImageFileResponse?> GetSignatureAsync(int employeeId, CancellationToken cancellationToken) =>
        dbContext.SignatureImageFiles.AsNoTracking()
            .Where(value => value.EmployeeId == employeeId)
            .Select(ToSignature())
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<SignatureImageFile?> CreateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Employees.AnyAsync(value => value.Id == employeeId, cancellationToken))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new SignatureImageFile
        {
            EmployeeId = employeeId,
            Bucket = request.Bucket.Trim(),
            ObjectName = request.ObjectName.Trim(),
            CreatedDate = now,
            ModifiedDate = now,
        };
        dbContext.SignatureImageFiles.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateSignatureAsync(int employeeId, UpsertSignatureImageFileRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SignatureImageFiles.SingleOrDefaultAsync(value => value.EmployeeId == employeeId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.EmployeeId = request.EmployeeId ?? employeeId;
        entity.Bucket = request.Bucket.Trim();
        entity.ObjectName = request.ObjectName.Trim();
        entity.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSignatureAsync(int employeeId, CancellationToken cancellationToken) =>
        await dbContext.SignatureImageFiles.Where(value => value.EmployeeId == employeeId).ExecuteDeleteAsync(cancellationToken) == 1;

    private static IQueryable<EmployeeResponse> Project(IQueryable<Employee> query) => query.Select(employee => new EmployeeResponse(
        employee.Id,
        employee.RoleId,
        employee.FirstName,
        employee.LastName,
        employee.FullName,
        employee.PhoneNumber,
        employee.Email,
        employee.DateOfBirth,
        employee.HomeAddressId,
        employee.CreatedDate,
        employee.ModifiedDate,
        employee.HomeAddress == null ? null : new AddressResponse(
            employee.HomeAddress.Id,
            employee.HomeAddress.Building,
            employee.HomeAddress.AddressLine1,
            employee.HomeAddress.AddressLine2,
            employee.HomeAddress.City,
            employee.HomeAddress.State,
            employee.HomeAddress.PostalCode,
            employee.HomeAddress.CountryId,
            employee.HomeAddress.CreatedDate,
            employee.HomeAddress.ModifiedDate),
        null));

    private static System.Linq.Expressions.Expression<Func<Address, AddressResponse>> ToAddress() => address => new AddressResponse(
        address.Id,
        address.Building,
        address.AddressLine1,
        address.AddressLine2,
        address.City,
        address.State,
        address.PostalCode,
        address.CountryId,
        address.CreatedDate,
        address.ModifiedDate);

    private static System.Linq.Expressions.Expression<Func<Role, RoleResponse>> ToRole() => role => new RoleResponse(
        role.Id, role.Name, role.Description, role.CreatedDate, role.ModifiedDate);

    private static System.Linq.Expressions.Expression<Func<SignatureImageFile, SignatureImageFileResponse>> ToSignature() => signature =>
        new SignatureImageFileResponse(signature.Id, signature.EmployeeId, signature.Bucket, signature.ObjectName, signature.CreatedDate, signature.ModifiedDate);
}
