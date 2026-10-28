using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenLethe.Data.Migrations
{
    /// <inheritdoc />
    public partial class TuneAccountsAutovacuum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every save rewrites a whole jsonb document, so the dead row versions live
            // almost entirely in the TOAST table. Postgres' defaults wait for 20% of a
            // table to be dead before vacuuming it; on a small row count with large
            // documents that lets the TOAST table bloat well past the live data. Vacuum
            // at 5% instead so the space is reused before the table grows.
            migrationBuilder.Sql("""
                ALTER TABLE accounts SET (
                    autovacuum_vacuum_scale_factor = 0.05,
                    toast.autovacuum_vacuum_scale_factor = 0.05
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE accounts RESET (
                    autovacuum_vacuum_scale_factor,
                    toast.autovacuum_vacuum_scale_factor
                );
                """);
        }
    }
}
