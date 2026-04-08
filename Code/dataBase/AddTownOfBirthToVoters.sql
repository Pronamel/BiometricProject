-- Add encrypted Town Of Birth storage for SDI v2 identity composition.
ALTER TABLE "Voters"
ADD COLUMN IF NOT EXISTS "TownOfBirth" bytea;
