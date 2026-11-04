using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PersonalHub.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                    table.CheckConstraint("CK_NotificationOutbox_Attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("CK_NotificationOutbox_Channel", "\"Channel\" IN ('WhatsApp')");
                    table.CheckConstraint("CK_NotificationOutbox_RecipientPhone_E164", "\"RecipientPhone\" IS NULL OR \"RecipientPhone\" ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.CheckConstraint("CK_NotificationOutbox_Status", "\"Status\" IN ('Pending', 'Processing', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "FK_NotificationOutbox_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonalProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalProfile", x => x.Id);
                    table.CheckConstraint("CK_PersonalProfile_PhoneNumber_E164", "\"PhoneNumber\" IS NULL OR \"PhoneNumber\" ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.ForeignKey(
                        name: "FK_PersonalProfile_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonalStudent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    StudentEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StudentPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LessonPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BillingDueDay = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalStudent", x => x.Id);
                    table.CheckConstraint("CK_PersonalStudent_BillingDueDay", "\"BillingDueDay\" BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_PersonalStudent_EndedAtUtc", "\"Status\" <> 'Inactive' OR \"EndedAtUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_PersonalStudent_LessonPrice", "\"LessonPrice\" > 0");
                    table.CheckConstraint("CK_PersonalStudent_Status", "\"Status\" IN ('Invited', 'Active', 'Inactive')");
                    table.CheckConstraint("CK_PersonalStudent_StudentEmail_Normalized", "\"StudentEmail\" = lower(btrim(\"StudentEmail\"))");
                    table.CheckConstraint("CK_PersonalStudent_StudentPhone_E164", "\"StudentPhone\" ~ '^\\+[1-9][0-9]{7,14}$'");
                    table.ForeignKey(
                        name: "FK_PersonalStudent_AspNetUsers_StudentUserId",
                        column: x => x.StudentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonalStudent_PersonalProfile_PersonalId",
                        column: x => x.PersonalId,
                        principalTable: "PersonalProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingPeriod",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalStudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPeriod", x => x.Id);
                    table.CheckConstraint("CK_BillingPeriod_PaidAtUtc", "(\"Status\" = 'Paid') = (\"PaidAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_BillingPeriod_Range", "\"PeriodEnd\" > \"PeriodStart\"");
                    table.CheckConstraint("CK_BillingPeriod_ReferenceMonth", "EXTRACT(DAY FROM \"ReferenceMonth\") = 1");
                    table.CheckConstraint("CK_BillingPeriod_Status", "\"Status\" IN ('Issued', 'PaymentReported', 'Paid')");
                    table.CheckConstraint("CK_BillingPeriod_TotalAmount", "\"TotalAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_BillingPeriod_PersonalStudent_PersonalStudentId",
                        column: x => x.PersonalStudentId,
                        principalTable: "PersonalStudent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Lesson",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalStudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<short>(type: "smallint", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lesson", x => x.Id);
                    table.CheckConstraint("CK_Lesson_Deletion", "(\"DeletedAtUtc\" IS NULL) = (\"DeletedByUserId\" IS NULL)");
                    table.CheckConstraint("CK_Lesson_DeletionReason", "\"DeletedAtUtc\" IS NULL OR \"Status\" = 'Pending' OR \"DeletionReason\" IS NOT NULL");
                    table.CheckConstraint("CK_Lesson_DurationMinutes", "\"DurationMinutes\" IS NULL OR \"DurationMinutes\" > 0");
                    table.CheckConstraint("CK_Lesson_RejectionReason", "\"Status\" <> 'Rejected' OR \"RejectionReason\" IS NOT NULL");
                    table.CheckConstraint("CK_Lesson_Response", "(\"RespondedAtUtc\" IS NULL) = (\"RespondedByUserId\" IS NULL)");
                    table.CheckConstraint("CK_Lesson_Status", "\"Status\" IN ('Pending', 'Approved', 'Rejected')");
                    table.CheckConstraint("CK_Lesson_UnitPrice", "\"UnitPrice\" > 0");
                    table.ForeignKey(
                        name: "FK_Lesson_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lesson_AspNetUsers_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lesson_AspNetUsers_RespondedByUserId",
                        column: x => x.RespondedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lesson_PersonalStudent_PersonalStudentId",
                        column: x => x.PersonalStudentId,
                        principalTable: "PersonalStudent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentInvite",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonalStudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentInvite", x => x.Id);
                    table.CheckConstraint("CK_StudentInvite_TokenHash_Length", "octet_length(\"TokenHash\") = 32");
                    table.ForeignKey(
                        name: "FK_StudentInvite_PersonalStudent_PersonalStudentId",
                        column: x => x.PersonalStudentId,
                        principalTable: "PersonalStudent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payment", x => x.Id);
                    table.CheckConstraint("CK_Payment_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Payment_Decision", "(\"DecidedAtUtc\" IS NULL) = (\"DecidedByUserId\" IS NULL) AND (\"Status\" = 'Reported') = (\"DecidedAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_Payment_Status", "\"Status\" IN ('Reported', 'Confirmed', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_Payment_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payment_AspNetUsers_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payment_BillingPeriod_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingPeriodItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LessonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPeriodItem", x => x.Id);
                    table.CheckConstraint("CK_BillingPeriodItem_Amount", "(\"Kind\" = 'Lesson' AND \"Amount\" > 0) OR (\"Kind\" = 'Adjustment' AND \"Amount\" < 0)");
                    table.CheckConstraint("CK_BillingPeriodItem_Kind", "\"Kind\" IN ('Lesson', 'Adjustment')");
                    table.CheckConstraint("CK_BillingPeriodItem_LessonId", "\"Kind\" <> 'Lesson' OR \"LessonId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_BillingPeriodItem_BillingPeriod_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "BillingPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingPeriodItem_Lesson_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lesson",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("6f1c2a52-3b7e-4d5a-9c1e-2f8a4b7d1001"), "b1e0d4c3-0001-4c5e-8a77-5e0f6a2d9001", "Personal", "PERSONAL" },
                    { new Guid("6f1c2a52-3b7e-4d5a-9c1e-2f8a4b7d1002"), "b1e0d4c3-0002-4c5e-8a77-5e0f6a2d9002", "Student", "STUDENT" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActorUserId",
                table: "AuditLog",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPeriod_PersonalStudentId_ReferenceMonth",
                table: "BillingPeriod",
                columns: new[] { "PersonalStudentId", "ReferenceMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPeriodItem_BillingPeriodId",
                table: "BillingPeriodItem",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPeriodItem_LessonId_Kind",
                table: "BillingPeriodItem",
                columns: new[] { "LessonId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lesson_CreatedByUserId",
                table: "Lesson",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Lesson_DeletedByUserId",
                table: "Lesson",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Lesson_PersonalStudentId_OccurredAtUtc",
                table: "Lesson",
                columns: new[] { "PersonalStudentId", "OccurredAtUtc" },
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Lesson_PersonalStudentId_Status",
                table: "Lesson",
                columns: new[] { "PersonalStudentId", "Status" },
                filter: "\"DeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Lesson_RespondedByUserId",
                table: "Lesson",
                column: "RespondedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_IdempotencyKey",
                table: "NotificationOutbox",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_RecipientUserId",
                table: "NotificationOutbox",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_NextAttemptAtUtc",
                table: "NotificationOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Payment_BillingPeriodId_Active",
                table: "Payment",
                column: "BillingPeriodId",
                unique: true,
                filter: "\"Status\" IN ('Reported', 'Confirmed')");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_DecidedByUserId",
                table: "Payment",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_ReportedByUserId",
                table: "Payment",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalProfile_UserId",
                table: "PersonalProfile",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalStudent_PersonalId_Status",
                table: "PersonalStudent",
                columns: new[] { "PersonalId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalStudent_PersonalId_StudentEmail",
                table: "PersonalStudent",
                columns: new[] { "PersonalId", "StudentEmail" },
                unique: true,
                filter: "\"Status\" <> 'Inactive'");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalStudent_PersonalId_StudentUserId",
                table: "PersonalStudent",
                columns: new[] { "PersonalId", "StudentUserId" },
                unique: true,
                filter: "\"StudentUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalStudent_StudentUserId_Status",
                table: "PersonalStudent",
                columns: new[] { "StudentUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvite_PersonalStudentId",
                table: "StudentInvite",
                column: "PersonalStudentId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvite_TokenHash",
                table: "StudentInvite",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "BillingPeriodItem");

            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropTable(
                name: "Payment");

            migrationBuilder.DropTable(
                name: "StudentInvite");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Lesson");

            migrationBuilder.DropTable(
                name: "BillingPeriod");

            migrationBuilder.DropTable(
                name: "PersonalStudent");

            migrationBuilder.DropTable(
                name: "PersonalProfile");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
