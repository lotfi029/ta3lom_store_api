using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// A-2: a notification is claimed as `sending`; one found still `sending` later becomes `unknown` (never sent again).
    /// Expand only: the CHECK list grows. Down folds both new states into `sent` (no resend) before restoring the old list.
    public partial class NotificationSendingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_status",
                schema: "store",
                table: "notification");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_status",
                schema: "store",
                table: "notification",
                sql: "status IN ('sent', 'failed', 'sending', 'unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE store.notification SET status = 'sent' WHERE status IN ('sending', 'unknown');");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_status",
                schema: "store",
                table: "notification");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_status",
                schema: "store",
                table: "notification",
                sql: "status IN ('sent', 'failed')");
        }
    }
}
