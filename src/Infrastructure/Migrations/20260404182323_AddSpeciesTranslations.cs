using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeciesTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeciesTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpeciesId = table.Column<int>(type: "int", nullable: false),
                    Locale = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CommonName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeciesTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeciesTranslations_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeciesTranslations_SpeciesId_Locale",
                table: "SpeciesTranslations",
                columns: new[] { "SpeciesId", "Locale" },
                unique: true);

            // Copy existing English common names to the new translations table
            migrationBuilder.Sql(
                "INSERT INTO SpeciesTranslations (SpeciesId, Locale, CommonName) SELECT Id, 'en', CommonName FROM Species WHERE CommonName IS NOT NULL;");

            // Now it is safe to drop the column
            migrationBuilder.DropColumn(
                name: "CommonName",
                table: "Species");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommonName",
                table: "Species",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE s SET s.CommonName = t.CommonName FROM Species s INNER JOIN SpeciesTranslations t ON s.Id = t.SpeciesId WHERE t.Locale = 'en';");

            migrationBuilder.DropTable(
                name: "SpeciesTranslations");

        }
    }
}
