using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ta3lom.Store.Infrastructure.Persistence.Migrations
{
    /// L-13: option labels become unique per project through a DEFERRABLE INITIALLY DEFERRED constraint, checked at commit,
    /// so two options can swap labels in one save. The constraint is raw SQL and not in the EF model (EF would order the
    /// updates by it and refuse a swap); the model has a plain index on project_id instead. Down restores the old unique
    /// index and leaves the extensions in place.
    public partial class OptionLabelDeferrable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_option_label",
                schema: "store",
                table: "project_option");

            migrationBuilder.Sql("""
                ALTER TABLE store.project_option
                    ADD CONSTRAINT ux_option_label UNIQUE (project_id, label_ar) DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_option_project",
                schema: "store",
                table: "project_option",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_option_project",
                schema: "store",
                table: "project_option");

            migrationBuilder.Sql("ALTER TABLE store.project_option DROP CONSTRAINT ux_option_label;");

            migrationBuilder.CreateIndex(
                name: "ux_option_label",
                schema: "store",
                table: "project_option",
                columns: new[] { "project_id", "label_ar" },
                unique: true);
        }
    }
}
