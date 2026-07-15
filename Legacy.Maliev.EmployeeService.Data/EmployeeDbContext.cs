using Legacy.Maliev.EmployeeService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.EmployeeService.Data;

/// <summary>PostgreSQL context preserving the legacy employee schema.</summary>
public sealed class EmployeeDbContext(DbContextOptions<EmployeeDbContext> options) : DbContext(options)
{
    /// <summary>Legacy employees.</summary>
    public DbSet<Employee> Employees => Set<Employee>();
    /// <summary>Legacy employee addresses.</summary>
    public DbSet<Address> Addresses => Set<Address>();
    /// <summary>Legacy employee roles.</summary>
    public DbSet<Role> Roles => Set<Role>();
    /// <summary>Legacy signature image metadata.</summary>
    public DbSet<SignatureImageFile> SignatureImageFiles => Set<SignatureImageFile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var address = modelBuilder.Entity<Address>();
        address.ToTable("Address");
        address.HasKey(value => value.Id);
        address.Property(value => value.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        address.Property(value => value.AddressLine1).HasMaxLength(256);
        address.Property(value => value.AddressLine2).HasMaxLength(256);
        address.Property(value => value.Building).HasMaxLength(256);
        address.Property(value => value.City).HasMaxLength(256);
        address.Property(value => value.CountryId).HasColumnName("CountryID");
        address.Property(value => value.PostalCode).HasMaxLength(256);
        address.Property(value => value.State).HasMaxLength(256);
        ConfigureDates(address);

        var role = modelBuilder.Entity<Role>();
        role.ToTable("Role");
        role.HasKey(value => value.Id);
        role.Property(value => value.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        role.Property(value => value.Name).HasMaxLength(50);
        role.Property(value => value.Description).HasMaxLength(50);
        ConfigureDates(role);

        var employee = modelBuilder.Entity<Employee>();
        employee.ToTable("Employee");
        employee.HasKey(value => value.Id);
        employee.Property(value => value.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        employee.Property(value => value.RoleId).HasColumnName("RoleID");
        employee.Property(value => value.HomeAddressId).HasColumnName("HomeAddressID");
        employee.Property(value => value.FirstName).HasMaxLength(256).IsRequired();
        employee.Property(value => value.LastName).HasMaxLength(256).IsRequired();
        employee.Property(value => value.FullName)
            .HasMaxLength(513)
            .HasComputedColumnSql("btrim(\"FirstName\" || ' ' || \"LastName\")", stored: true);
        employee.Property(value => value.PhoneNumber).HasMaxLength(256);
        employee.Property(value => value.Email).HasMaxLength(256).IsRequired();
        employee.Property(value => value.DateOfBirth).HasColumnType("date");
        ConfigureDates(employee);
        employee.HasOne(value => value.HomeAddress)
            .WithMany(value => value.Employees)
            .HasForeignKey(value => value.HomeAddressId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Employee_Address");
        employee.HasOne(value => value.Role)
            .WithMany(value => value.Employees)
            .HasForeignKey(value => value.RoleId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Employee_Role");

        var signature = modelBuilder.Entity<SignatureImageFile>();
        signature.ToTable("SignatureImageFile");
        signature.HasKey(value => value.Id);
        signature.Property(value => value.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        signature.Property(value => value.EmployeeId).HasColumnName("EmployeeID");
        signature.Property(value => value.Bucket).HasMaxLength(50).IsRequired();
        signature.Property(value => value.ObjectName).IsRequired();
        ConfigureDates(signature);
    }

    private static void ConfigureDates<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.Property<DateTime?>(nameof(Employee.CreatedDate)).HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property<DateTime?>(nameof(Employee.ModifiedDate)).HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}
