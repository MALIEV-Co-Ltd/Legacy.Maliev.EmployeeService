namespace Legacy.Maliev.EmployeeService.Domain;

/// <summary>Legacy employee record used by staff and document workflows.</summary>
public sealed class Employee
{
    /// <summary>Legacy employee identifier.</summary>
    public int Id { get; set; }
    /// <summary>Optional role identifier.</summary>
    public int? RoleId { get; set; }
    /// <summary>Employee given name.</summary>
    public string FirstName { get; set; } = string.Empty;
    /// <summary>Employee family name.</summary>
    public string LastName { get; set; } = string.Empty;
    /// <summary>Database-computed display name.</summary>
    public string FullName { get; private set; } = string.Empty;
    /// <summary>Employee phone number.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>Employee email address.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Optional date of birth.</summary>
    public DateTime? DateOfBirth { get; set; }
    /// <summary>Optional home address identifier.</summary>
    public int? HomeAddressId { get; set; }
    /// <summary>UTC creation time.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>UTC last-modified time.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Related home address.</summary>
    public Address? HomeAddress { get; set; }
    /// <summary>Related business role.</summary>
    public Role? Role { get; set; }
}

/// <summary>Legacy employee home address.</summary>
public sealed class Address
{
    /// <summary>Legacy address identifier.</summary>
    public int Id { get; set; }
    /// <summary>Optional building name.</summary>
    public string? Building { get; set; }
    /// <summary>Primary address line.</summary>
    public string? AddressLine1 { get; set; }
    /// <summary>Secondary address line.</summary>
    public string? AddressLine2 { get; set; }
    /// <summary>City or locality.</summary>
    public string? City { get; set; }
    /// <summary>State or province.</summary>
    public string? State { get; set; }
    /// <summary>Postal code.</summary>
    public string? PostalCode { get; set; }
    /// <summary>Legacy country identifier.</summary>
    public int CountryId { get; set; }
    /// <summary>UTC creation time.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>UTC last-modified time.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Employees using this home address.</summary>
    public ICollection<Employee> Employees { get; } = [];
}

/// <summary>Legacy employee role.</summary>
public sealed class Role
{
    /// <summary>Legacy role identifier.</summary>
    public int Id { get; set; }
    /// <summary>Role name.</summary>
    public string? Name { get; set; }
    /// <summary>Role description.</summary>
    public string? Description { get; set; }
    /// <summary>UTC creation time.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>UTC last-modified time.</summary>
    public DateTime? ModifiedDate { get; set; }
    /// <summary>Employees assigned to this role.</summary>
    public ICollection<Employee> Employees { get; } = [];
}

/// <summary>Google Cloud Storage object metadata for an employee signature image.</summary>
public sealed class SignatureImageFile
{
    /// <summary>Legacy signature record identifier.</summary>
    public int Id { get; set; }
    /// <summary>Employee owning the signature.</summary>
    public int EmployeeId { get; set; }
    /// <summary>Google Cloud Storage bucket.</summary>
    public string Bucket { get; set; } = string.Empty;
    /// <summary>Google Cloud Storage object name.</summary>
    public string ObjectName { get; set; } = string.Empty;
    /// <summary>UTC creation time.</summary>
    public DateTime? CreatedDate { get; set; }
    /// <summary>UTC last-modified time.</summary>
    public DateTime? ModifiedDate { get; set; }
}
