-- Legacy cleanup statements
ALTER TABLE "Voters" DROP COLUMN IF EXISTS "ElectoralRollNumber";
ALTER TABLE "Voters" DROP COLUMN IF EXISTS "AddressLine1";
ALTER TABLE "Voters" DROP COLUMN IF EXISTS "PreviousAddress";
ALTER TABLE "PollingStations" DROP COLUMN IF EXISTS "TotalVotes";
