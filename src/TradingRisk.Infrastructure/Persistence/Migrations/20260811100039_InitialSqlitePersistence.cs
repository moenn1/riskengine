using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingRisk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlitePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Portfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PortfolioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstrumentId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Positions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_BaseCurrency",
                table: "Portfolios",
                column: "BaseCurrency");

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_Name",
                table: "Portfolios",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_InstrumentId",
                table: "Positions",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PortfolioId_InstrumentId",
                table: "Positions",
                columns: new[] { "PortfolioId", "InstrumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "Portfolios");
        }
    }
}
