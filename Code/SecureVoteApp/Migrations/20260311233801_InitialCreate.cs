using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureVoteApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Constituencies",
                columns: table => new
                {
                    ConstituencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TotalVoters = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Constituencies", x => x.ConstituencyId);
                });

            migrationBuilder.CreateTable(
                name: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ElectionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ElectionType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Elections", x => x.ElectionId);
                });

            migrationBuilder.CreateTable(
                name: "PollingStations",
                columns: table => new
                {
                    PollingStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PollingStationCode = table.Column<Guid>(type: "uuid", nullable: false),
                    ConstituencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    County = table.Column<string>(type: "text", nullable: true),
                    InvalidVotes = table.Column<int>(type: "integer", nullable: false),
                    TotalVotes = table.Column<int>(type: "integer", nullable: false),
                    ExpectedVotes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollingStations", x => x.PollingStationId);
                    table.ForeignKey(
                        name: "FK_PollingStations_Constituencies_ConstituencyId",
                        column: x => x.ConstituencyId,
                        principalTable: "Constituencies",
                        principalColumn: "ConstituencyId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Voters",
                columns: table => new
                {
                    VoterId = table.Column<Guid>(type: "uuid", nullable: false),
                    NationalId = table.Column<string>(type: "text", nullable: false),
                    ElectoralRollNumber = table.Column<Guid>(type: "uuid", nullable: false),
                    ConstituencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WardId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddressLine1 = table.Column<string>(type: "text", nullable: false),
                    PreviousAddress = table.Column<string>(type: "text", nullable: false),
                    Postcode = table.Column<string>(type: "text", nullable: false),
                    FingerprintScan = table.Column<byte[]>(type: "bytea", nullable: true),
                    HasVoted = table.Column<bool>(type: "boolean", nullable: false),
                    RegisteredDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Voters", x => x.VoterId);
                    table.ForeignKey(
                        name: "FK_Voters_Constituencies_ConstituencyId",
                        column: x => x.ConstituencyId,
                        principalTable: "Constituencies",
                        principalColumn: "ConstituencyId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Candidates",
                columns: table => new
                {
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Party = table.Column<string>(type: "text", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    ConstituencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidates", x => x.CandidateId);
                    table.ForeignKey(
                        name: "FK_Candidates_Constituencies_ConstituencyId",
                        column: x => x.ConstituencyId,
                        principalTable: "Constituencies",
                        principalColumn: "ConstituencyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Candidates_Elections_ElectionId",
                        column: x => x.ElectionId,
                        principalTable: "Elections",
                        principalColumn: "ElectionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoteRecords",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VotedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoteRecords", x => x.RecordId);
                    table.ForeignKey(
                        name: "FK_VoteRecords_Elections_ElectionId",
                        column: x => x.ElectionId,
                        principalTable: "Elections",
                        principalColumn: "ElectionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Officials",
                columns: table => new
                {
                    OfficialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    LastLogin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedCountyId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedPollingStationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Officials", x => x.OfficialId);
                    table.ForeignKey(
                        name: "FK_Officials_PollingStations_AssignedPollingStationId",
                        column: x => x.AssignedPollingStationId,
                        principalTable: "PollingStations",
                        principalColumn: "PollingStationId");
                });

            migrationBuilder.CreateTable(
                name: "ConstituencyResults",
                columns: table => new
                {
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConstituencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalVotes = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConstituencyResults", x => x.ResultId);
                    table.ForeignKey(
                        name: "FK_ConstituencyResults_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "CandidateId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConstituencyResults_Constituencies_ConstituencyId",
                        column: x => x.ConstituencyId,
                        principalTable: "Constituencies",
                        principalColumn: "ConstituencyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConstituencyResults_Elections_ElectionId",
                        column: x => x.ElectionId,
                        principalTable: "Elections",
                        principalColumn: "ElectionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_ConstituencyId_ElectionId_FirstName_LastName",
                table: "Candidates",
                columns: new[] { "ConstituencyId", "ElectionId", "FirstName", "LastName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_ElectionId",
                table: "Candidates",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstituencyResults_CandidateId",
                table: "ConstituencyResults",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstituencyResults_ConstituencyId_CandidateId_ElectionId",
                table: "ConstituencyResults",
                columns: new[] { "ConstituencyId", "CandidateId", "ElectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConstituencyResults_ElectionId",
                table: "ConstituencyResults",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Officials_AssignedPollingStationId",
                table: "Officials",
                column: "AssignedPollingStationId");

            migrationBuilder.CreateIndex(
                name: "IX_PollingStations_ConstituencyId",
                table: "PollingStations",
                column: "ConstituencyId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteRecords_ElectionId",
                table: "VoteRecords",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Voters_ConstituencyId",
                table: "Voters",
                column: "ConstituencyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConstituencyResults");

            migrationBuilder.DropTable(
                name: "Officials");

            migrationBuilder.DropTable(
                name: "VoteRecords");

            migrationBuilder.DropTable(
                name: "Voters");

            migrationBuilder.DropTable(
                name: "Candidates");

            migrationBuilder.DropTable(
                name: "PollingStations");

            migrationBuilder.DropTable(
                name: "Elections");

            migrationBuilder.DropTable(
                name: "Constituencies");
        }
    }
}
