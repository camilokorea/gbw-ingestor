using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddINatTaxonIdAndObservationCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "INatTaxonId",
                table: "Species",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObservationCount",
                table: "Species",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "INatTaxonId",
                table: "Species");

            migrationBuilder.DropColumn(
                name: "ObservationCount",
                table: "Species");
        }
    }
}
