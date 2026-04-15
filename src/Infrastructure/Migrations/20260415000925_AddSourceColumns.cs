using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Observations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "ebird");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "IngestionQueue",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "ebird");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionQueue_Source",
                table: "IngestionQueue",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IngestionQueue_Source",
                table: "IngestionQueue");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "IngestionQueue");
        }
    }
}
