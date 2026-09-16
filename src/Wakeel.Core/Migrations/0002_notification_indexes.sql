-- B2 «الخدمات» — the notification table is read on every minute tick.
--
-- ReminderScheduler.RunOnceAsync loads the reminder keys already used inside its retention
-- window so a reminder is raised once and only once; NotificationService looks a single source
-- key up when it creates a row; and the bell panel orders the table by created_at. With only
-- ix_notifications_due_at in place all three were full scans that grow for the life of the
-- installation, on a table nothing ever prunes (dismissed rows are kept deliberately — they are
-- what makes the reminder keys idempotent).
--
-- source is indexed first so the prefix match on «reminder:» is a range scan, with created_at as
-- the second column so the retention floor is applied inside the same index.

CREATE INDEX IF NOT EXISTS ix_notifications_source ON notifications(source, created_at);
CREATE INDEX IF NOT EXISTS ix_notifications_created_at ON notifications(created_at);
