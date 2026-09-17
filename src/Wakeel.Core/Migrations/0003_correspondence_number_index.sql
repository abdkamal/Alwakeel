-- B3-1 «المراسلات» — the official-number uniqueness index is per SEQUENCE, not global.
--
-- ARCHITECTURE.md §5 and AGREEMENT item 5 define TWO independent yearly sequences, one for
-- incoming and one for outgoing («تسلسلان مستقلان (صادر/وارد)»), and the number itself —
-- YYYYMMDD/DESSS — records the date, the device, the employee and the sequence position, but
-- NOT which of the two sequences it came from. The first outgoing letter of a day and the first
-- incoming one of that same day on the same device therefore carry the very same text, exactly
-- as intended.
--
-- ux_correspondence_official_number as created in 0001_initial.sql was UNIQUE on
-- official_number alone, which made that ordinary pair impossible: registering an incoming
-- letter and approving an outgoing one on the same day raised a constraint failure on the
-- second of the two, inside the transaction that had just issued its number. The index is
-- replaced here with the same guarantee measured per sequence: within one direction a number is
-- still issued exactly once and never reissued.
--
-- Like the original it is deliberately NOT filtered on deleted_at: a number stays consumed
-- forever, even against a soft-deleted row.

DROP INDEX IF EXISTS ux_correspondence_official_number;

CREATE UNIQUE INDEX IF NOT EXISTS ux_correspondence_direction_official_number
  ON correspondence(direction, official_number)
  WHERE official_number IS NOT NULL;
