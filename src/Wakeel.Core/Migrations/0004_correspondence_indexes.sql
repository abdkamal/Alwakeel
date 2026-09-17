-- B3-1 «المراسلات» — indexes for the reads the correspondence services make on every
-- registration, every minute tick and every open of the exchange screen.
--
-- correspondence(direction, external_number): the duplicate check of AGREEMENT item 14 runs on
-- EVERY incoming registration and looks the party's own number up among the items of the same
-- direction. Without this it is a full scan of the correspondence table, growing for the life of
-- the office, on the hottest path of the incoming screen.
--
-- followups(status_to, correspondence_id): the «بانتظار رد منذ N أيام» cards find, for each
-- waiting item, the newest entry that moved it into that state. The existing
-- ix_followups_correspondence_id cannot serve that filter, which selects on status_to first.
--
-- followups(reminder_at): the follow-up reminder pass reads the entries whose reminder instant
-- falls inside its lookback window, once a minute, forever.
--
-- duplicate_reviews(correspondence_id): the reviews of one item are read whenever it is opened;
-- 0001_initial.sql created the table with no index at all.
--
-- exchange_log(at): W93 lists the exchange newest first.

CREATE INDEX IF NOT EXISTS ix_correspondence_external_number
  ON correspondence(direction, external_number);

CREATE INDEX IF NOT EXISTS ix_followups_status_to
  ON followups(status_to, correspondence_id);

CREATE INDEX IF NOT EXISTS ix_followups_reminder_at
  ON followups(reminder_at);

CREATE INDEX IF NOT EXISTS ix_duplicate_reviews_correspondence_id
  ON duplicate_reviews(correspondence_id);

CREATE INDEX IF NOT EXISTS ix_exchange_log_at
  ON exchange_log(at);
