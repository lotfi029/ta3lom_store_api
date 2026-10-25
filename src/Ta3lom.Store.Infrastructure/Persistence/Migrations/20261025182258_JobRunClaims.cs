using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// L-4: `job_run` (job, fire_time) so a scheduled job runs once across instances. Expand only.
    public partial class JobRunClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_run",
                schema: "store",
                columns: table => new
                {
                    job = table.Column<string>(type: "text", nullable: false),
                    fire_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_run", x => new { x.job, x.fire_time });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_run",
                schema: "store");
        }
    }
}
