using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Model only (task 39e follow-up): the reference keys of tasks 39b-39e move from DeleteBehavior.NoAction to
    /// ClientNoAction. The database keeps ON DELETE NO ACTION, so there is no DDL; EF no longer clears a tracked child's
    /// key when the soft-delete interceptor removes its parent. See ReferenceForeignKeys.Fk.
    /// </summary>
    public partial class ReferenceKeysClientNoAction39e : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
