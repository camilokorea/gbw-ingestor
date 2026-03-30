using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFlickrEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlickrPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PhotoId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WebUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SquareThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LargeSquareThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmallUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Small320Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MediumUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Medium640Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Medium800Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LargeUrl = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlickrPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlickrPhotoLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FlickrPhotoId = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    Accuracy = table.Column<int>(type: "int", nullable: true),
                    Context = table.Column<int>(type: "int", nullable: true),
                    PlaceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WoeId = table.Column<long>(type: "bigint", nullable: true),
                    LocalityName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocalityPlaceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocalityWoeId = table.Column<long>(type: "bigint", nullable: true),
                    CountyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountyPlaceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountyWoeId = table.Column<long>(type: "bigint", nullable: true),
                    RegionName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegionPlaceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegionWoeId = table.Column<long>(type: "bigint", nullable: true),
                    CountryName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountryPlaceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CountryWoeId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlickrPhotoLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlickrPhotoLocations_FlickrPhotos_FlickrPhotoId",
                        column: x => x.FlickrPhotoId,
                        principalTable: "FlickrPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeciesPhotos",
                columns: table => new
                {
                    SpeciesId = table.Column<int>(type: "int", nullable: false),
                    FlickrPhotoId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeciesPhotos", x => new { x.SpeciesId, x.FlickrPhotoId });
                    table.ForeignKey(
                        name: "FK_SpeciesPhotos_FlickrPhotos_FlickrPhotoId",
                        column: x => x.FlickrPhotoId,
                        principalTable: "FlickrPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpeciesPhotos_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlickrPhotoLocations_FlickrPhotoId",
                table: "FlickrPhotoLocations",
                column: "FlickrPhotoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlickrPhotos_PhotoId",
                table: "FlickrPhotos",
                column: "PhotoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeciesPhotos_FlickrPhotoId",
                table: "SpeciesPhotos",
                column: "FlickrPhotoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlickrPhotoLocations");

            migrationBuilder.DropTable(
                name: "SpeciesPhotos");

            migrationBuilder.DropTable(
                name: "FlickrPhotos");
        }
    }
}
