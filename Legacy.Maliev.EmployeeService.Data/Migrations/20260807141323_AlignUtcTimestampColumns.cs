using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.EmployeeService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignUtcTimestampColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Address" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Address" ALTER COLUMN "CreatedDate" TYPE timestamp without time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Address" ALTER COLUMN "CreatedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "Address" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Address" ALTER COLUMN "ModifiedDate" TYPE timestamp without time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Address" ALTER COLUMN "ModifiedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "Role" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Role" ALTER COLUMN "CreatedDate" TYPE timestamp without time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Role" ALTER COLUMN "CreatedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "Role" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Role" ALTER COLUMN "ModifiedDate" TYPE timestamp without time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Role" ALTER COLUMN "ModifiedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "Employee" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Employee" ALTER COLUMN "CreatedDate" TYPE timestamp without time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Employee" ALTER COLUMN "CreatedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "Employee" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Employee" ALTER COLUMN "ModifiedDate" TYPE timestamp without time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Employee" ALTER COLUMN "ModifiedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "CreatedDate" TYPE timestamp without time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "CreatedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "ModifiedDate" TYPE timestamp without time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "ModifiedDate" SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Address" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Address" ALTER COLUMN "CreatedDate" TYPE timestamp with time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Address" ALTER COLUMN "CreatedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "Address" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Address" ALTER COLUMN "ModifiedDate" TYPE timestamp with time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Address" ALTER COLUMN "ModifiedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "Role" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Role" ALTER COLUMN "CreatedDate" TYPE timestamp with time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Role" ALTER COLUMN "CreatedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "Role" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Role" ALTER COLUMN "ModifiedDate" TYPE timestamp with time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Role" ALTER COLUMN "ModifiedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "Employee" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Employee" ALTER COLUMN "CreatedDate" TYPE timestamp with time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Employee" ALTER COLUMN "CreatedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "Employee" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Employee" ALTER COLUMN "ModifiedDate" TYPE timestamp with time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Employee" ALTER COLUMN "ModifiedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "CreatedDate" TYPE timestamp with time zone USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "CreatedDate" SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "ModifiedDate" TYPE timestamp with time zone USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "SignatureImageFile" ALTER COLUMN "ModifiedDate" SET DEFAULT CURRENT_TIMESTAMP;
                """);
        }
    }
}
