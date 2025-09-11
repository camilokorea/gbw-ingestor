using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eBird.Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundSignatureEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SoundSignatures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    XenoCantoId = table.Column<int>(type: "int", nullable: false),
                    SoundType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AudioUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordistUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    License = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quality = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SpeciesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoundSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoundSignatures_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoundSignatures_SpeciesId",
                table: "SoundSignatures",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_SoundSignatures_XenoCantoId",
                table: "SoundSignatures",
                column: "XenoCantoId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SoundSignatures");
        }
    }
}
