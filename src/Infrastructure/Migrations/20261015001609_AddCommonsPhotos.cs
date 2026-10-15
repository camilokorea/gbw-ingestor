using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommonsPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommonsPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpeciesId = table.Column<int>(type: "int", nullable: false),
                    WikidataId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileTitle = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    DescriptionUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Artist = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LicenseCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LicenseShortName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LicenseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonsPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonsPhotos_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommonsPhotos_SpeciesId_FileTitle",
                table: "CommonsPhotos",
                columns: new[] { "SpeciesId", "FileTitle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonsPhotos");
        }
    }
}
