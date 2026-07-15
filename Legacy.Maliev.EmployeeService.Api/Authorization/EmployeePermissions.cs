namespace Legacy.Maliev.EmployeeService.Api.Authorization;

/// <summary>Granular staff-only permissions for legacy employee operations.</summary>
public static class EmployeePermissions
{
    /// <summary>Reads one employee.</summary>
    public const string EmployeesRead = "legacy-employee.employees.read";
    /// <summary>Lists employees.</summary>
    public const string EmployeesList = "legacy-employee.employees.list";
    /// <summary>Creates employees.</summary>
    public const string EmployeesCreate = "legacy-employee.employees.create";
    /// <summary>Updates employees.</summary>
    public const string EmployeesUpdate = "legacy-employee.employees.update";
    /// <summary>Deletes employees.</summary>
    public const string EmployeesDelete = "legacy-employee.employees.delete";
    /// <summary>Reads employee addresses.</summary>
    public const string AddressesRead = "legacy-employee.addresses.read";
    /// <summary>Lists employee addresses.</summary>
    public const string AddressesList = "legacy-employee.addresses.list";
    /// <summary>Creates employee addresses.</summary>
    public const string AddressesCreate = "legacy-employee.addresses.create";
    /// <summary>Updates employee addresses.</summary>
    public const string AddressesUpdate = "legacy-employee.addresses.update";
    /// <summary>Deletes employee addresses.</summary>
    public const string AddressesDelete = "legacy-employee.addresses.delete";
    /// <summary>Reads employee roles.</summary>
    public const string RolesRead = "legacy-employee.roles.read";
    /// <summary>Creates employee roles.</summary>
    public const string RolesCreate = "legacy-employee.roles.create";
    /// <summary>Updates employee roles.</summary>
    public const string RolesUpdate = "legacy-employee.roles.update";
    /// <summary>Deletes employee roles.</summary>
    public const string RolesDelete = "legacy-employee.roles.delete";
    /// <summary>Reads signature metadata.</summary>
    public const string SignaturesRead = "legacy-employee.signatures.read";
    /// <summary>Creates or changes signature metadata.</summary>
    public const string SignaturesWrite = "legacy-employee.signatures.write";
    /// <summary>Deletes signature metadata.</summary>
    public const string SignaturesDelete = "legacy-employee.signatures.delete";
    /// <summary>Reads safe employee identity data.</summary>
    public const string IdentitiesRead = "legacy-employee.identities.read";
    /// <summary>Manages employee identities.</summary>
    public const string IdentitiesManage = "legacy-employee.identities.manage";
    /// <summary>Performs critical live credential validation.</summary>
    public const string CredentialsValidate = "legacy-employee.credentials.validate";
}
