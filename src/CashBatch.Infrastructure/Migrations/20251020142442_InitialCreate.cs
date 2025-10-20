using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashBatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_batches",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceFilename = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cash_customer_lookups",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    KeyValue = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_customer_lookups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cash_match_logs",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_match_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cash_payments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CheckNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BankAccount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RemitAddressHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_payments_cash_batches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "dbo",
                        principalTable: "cash_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cash_payment_lines",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WasAutoMatched = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_payment_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_payment_lines_cash_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "dbo",
                        principalTable: "cash_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_customer_lookups_KeyType_KeyValue",
                schema: "dbo",
                table: "cash_customer_lookups",
                columns: new[] { "KeyType", "KeyValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_payment_lines_PaymentId",
                schema: "dbo",
                table: "cash_payment_lines",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_payments_BatchId",
                schema: "dbo",
                table: "cash_payments",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_customer_lookups",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "cash_match_logs",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "cash_payment_lines",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "cash_payments",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "cash_batches",
                schema: "dbo");
        }
    }
}
