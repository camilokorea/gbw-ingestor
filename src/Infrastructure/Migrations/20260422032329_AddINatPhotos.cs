using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddINatPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "INatPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PhotoId = table.Column<long>(type: "bigint", nullable: false),
                    Attribution = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LicenseCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    INatObservationId = table.Column<long>(type: "bigint", nullable: true),
                    SquareUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmallUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MediumUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LargeUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WebUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INatPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeciesINatPhotos",
                columns: table => new
                {
                    SpeciesId = table.Column<int>(type: "int", nullable: false),
                    INatPhotoId = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeciesINatPhotos", x => new { x.SpeciesId, x.INatPhotoId });
                    table.ForeignKey(
                        name: "FK_SpeciesINatPhotos_INatPhotos_INatPhotoId",
                        column: x => x.INatPhotoId,
                        principalTable: "INatPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpeciesINatPhotos_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_INatPhotos_PhotoId",
                table: "INatPhotos",
                column: "PhotoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeciesINatPhotos_INatPhotoId",
                table: "SpeciesINatPhotos",
                column: "INatPhotoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeciesINatPhotos");

            migrationBuilder.DropTable(
                name: "INatPhotos");
        }
    }
}
