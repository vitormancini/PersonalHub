using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalHub.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RecipientUserId",
                table: "NotificationOutbox",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderMessageId",
                table: "NotificationOutbox",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                table: "NotificationOutbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "NotificationOutbox",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "NotificationOutbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAuthentication",
                table: "NotificationOutbox",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "NotificationOutbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "NotificationOutbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PhoneVerificationCode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PersonalStudentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneE164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneVerificationCode", x => x.Id);
                    table.CheckConstraint("CK_PhoneVerificationCode_Attempts", "\"Attempts\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_PhoneVerificationCode_Hash", "octet_length(\"CodeHash\") = 32");
                    table.CheckConstraint("CK_PhoneVerificationCode_Phone_E164", "\"PhoneE164\" ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("CK_PhoneVerificationCode_Purpose", "\"Purpose\" IN ('InviteAcceptance', 'PhoneConfirmation', 'PasswordReset')");
                    table.CheckConstraint("CK_PhoneVerificationCode_Scope", "(\"Purpose\" = 'InviteAcceptance' AND \"PersonalStudentId\" IS NOT NULL AND \"UserId\" IS NULL) OR (\"Purpose\" <> 'InviteAcceptance' AND \"UserId\" IS NOT NULL AND \"PersonalStudentId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PhoneVerificationCode_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PhoneVerificationCode_PersonalStudent_PersonalStudentId",
                        column: x => x.PersonalStudentId,
                        principalTable: "PersonalStudent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_ProviderMessageId",
                table: "NotificationOutbox",
                column: "ProviderMessageId",
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationOutbox_DeliveryStatus",
                table: "NotificationOutbox",
                sql: "\"DeliveryStatus\" IS NULL OR \"DeliveryStatus\" IN ('Sent', 'Delivered', 'Read', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationOutbox_Lease",
                table: "NotificationOutbox",
                sql: "\"Status\" <> 'Processing' OR \"LeaseExpiresAtUtc\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NotificationOutbox_Sent",
                table: "NotificationOutbox",
                sql: "\"Status\" <> 'Sent' OR \"SentAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationCode_PersonalStudentId_CreatedAtUtc",
                table: "PhoneVerificationCode",
                columns: new[] { "PersonalStudentId", "CreatedAtUtc" },
                filter: "\"PersonalStudentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationCode_PhoneE164_Purpose_CreatedAtUtc",
                table: "PhoneVerificationCode",
                columns: new[] { "PhoneE164", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationCode_UserId_Purpose_CreatedAtUtc",
                table: "PhoneVerificationCode",
                columns: new[] { "UserId", "Purpose", "CreatedAtUtc" },
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhoneVerificationCode");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_ProviderMessageId",
                table: "NotificationOutbox");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationOutbox_DeliveryStatus",
                table: "NotificationOutbox");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationOutbox_Lease",
                table: "NotificationOutbox");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NotificationOutbox_Sent",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "IsAuthentication",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "NotificationOutbox");

            migrationBuilder.AlterColumn<Guid>(
                name: "RecipientUserId",
                table: "NotificationOutbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderMessageId",
                table: "NotificationOutbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
