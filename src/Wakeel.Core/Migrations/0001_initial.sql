-- الوكيل v0.21 — Wakeel.Core schema, migration 0001 (initial).
-- Mirrors docs/build/DATA-MODEL.md sections 0-12 (the Wakeel database only).
-- All dates/times are TEXT ISO-8601 UTC; ids are TEXT UUID v7; money is INTEGER agorot
-- (1 ₪ = 100); booleans are INTEGER 0/1; enums are TEXT snake_case.
-- Applied once by SchemaMigrator, which records it in schema_versions.
--
-- UNIQUE-INDEX POLICY FOR SOFT-DELETED TABLES (DATA-MODEL.md §0, decision of 2026-09-16).
-- One policy, applied everywhere: unique indexes on official identifiers
-- (correspondence.official_number, assets.inventory_number, financial_cycles.start_date,
-- monthly_reports.cycle_id) are UNFILTERED, i.e. they also cover soft-deleted rows, because an
-- official identifier must stay unique forever and is never reissued. The services that create
-- such rows therefore look the identifier up with IgnoreDeleted() and handle the "a hidden
-- (soft-deleted) row already owns this identifier" case explicitly — by restoring the hidden
-- row, or by reporting it to the user in Arabic — instead of letting the insert fail with an
-- unmapped SQLite UNIQUE error. Partial (WHERE deleted_at IS NULL) unique indexes are NOT used
-- on these columns: they would let a new row reuse a deleted row's official identifier and
-- collide on sync with a device that still holds the old row.
--
-- CHANGE_LOG DEVICE SEMANTICS (DATA-MODEL.md §11, confirmed 2026-09-16): change_log.device is
-- the device that performed the operation locally (installation.device_id), NOT the row's own
-- origin_device, so that a date-range export selects what changed on this device.
--
-- JSON COLUMN NAMES (confirmed 2026-09-16): JSON-valued columns keep the plain names given in
-- DATA-MODEL.md (cc, words, changes, fields, agenda, totals, overrides, asset_ids, counted,
-- sections, readiness, filters, counts, ours, theirs, items, details) with no _json suffix.

-- ============================================================================
-- §1 — identity, directory, devices, account, settings, numbering, audit,
-- notifications, clock checks, health snapshots. Local tables (not synced).
-- ============================================================================

CREATE TABLE installation (
  id TEXT PRIMARY KEY,
  org_id TEXT NOT NULL,
  org_name TEXT NOT NULL,
  logo_document_id TEXT,
  office_id TEXT NOT NULL,
  office_name TEXT NOT NULL,
  office_unit_id TEXT NOT NULL,
  office_code TEXT NOT NULL,
  device_id TEXT NOT NULL,
  device_no INTEGER NOT NULL,
  employee_no INTEGER NOT NULL,
  employee_name TEXT NOT NULL,
  role TEXT NOT NULL,
  sync_scope TEXT NOT NULL,
  cycle_start_day INTEGER NOT NULL,
  numbering_format TEXT NOT NULL,
  setup_version TEXT NOT NULL,
  activated_at TEXT NOT NULL,
  app_version TEXT NOT NULL,
  build_date TEXT NOT NULL,
  org_x25519_pub BLOB NOT NULL,
  org_ed25519_pub BLOB NOT NULL
);

CREATE TABLE org_units (
  id TEXT PRIMARY KEY,
  parent_id TEXT REFERENCES org_units(id),
  level TEXT NOT NULL,
  name TEXT NOT NULL,
  head_name TEXT,
  head_title TEXT,
  office_code TEXT,
  sort_order INTEGER NOT NULL DEFAULT 0,
  x25519_pub BLOB
);
CREATE INDEX ix_org_units_parent_id ON org_units(parent_id);

CREATE TABLE devices (
  id TEXT PRIMARY KEY,
  unit_id TEXT REFERENCES org_units(id),
  device_no INTEGER NOT NULL,
  employee_no INTEGER NOT NULL,
  employee_name TEXT NOT NULL,
  role TEXT NOT NULL,
  kind TEXT NOT NULL,
  ed25519_pub BLOB NOT NULL,
  x25519_pub BLOB NOT NULL,
  certificate BLOB NOT NULL,
  issued_at TEXT NOT NULL,
  revoked_at TEXT,
  paired_at TEXT,
  last_sync_at TEXT,
  sync_scope TEXT NOT NULL
);

CREATE TABLE account (
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL,
  display_name TEXT NOT NULL,
  photo_document_id TEXT,
  password_changed_at TEXT,
  failed_attempts INTEGER NOT NULL DEFAULT 0,
  locked_until TEXT,
  auto_lock_minutes INTEGER NOT NULL DEFAULT 10
);

CREATE TABLE settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE official_numbers (
  kind TEXT NOT NULL,
  year INTEGER NOT NULL,
  last_seq INTEGER NOT NULL DEFAULT 0,
  last_date TEXT NOT NULL,
  PRIMARY KEY (kind, year)
);

-- audit_log is a LOCAL, append-only table (DATA-MODEL.md §1, decision of 2026-09-16): it carries
-- none of the §0 columns and has no change_log trigger. B6 exports it read-only by its own `at`
-- date range, not through change_log like the synced tables; on import a row is INSERTed when
-- its id is absent and is never updated, so audit entries never conflict and are never rewritten.
CREATE TABLE audit_log (
  id TEXT PRIMARY KEY,
  at TEXT NOT NULL,
  actor TEXT NOT NULL,
  action TEXT NOT NULL,
  entity_type TEXT,
  entity_id TEXT,
  summary_ar TEXT NOT NULL,
  details TEXT
);
CREATE INDEX ix_audit_log_at ON audit_log(at);
CREATE INDEX ix_audit_log_entity ON audit_log(entity_type, entity_id);

CREATE TABLE notifications (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  title TEXT NOT NULL,
  body TEXT,
  entity_type TEXT,
  entity_id TEXT,
  created_at TEXT NOT NULL,
  due_at TEXT,
  read_at TEXT,
  dismissed_at TEXT,
  source TEXT
);
CREATE INDEX ix_notifications_due_at ON notifications(due_at);

CREATE TABLE clock_checks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  at TEXT NOT NULL,
  verdict TEXT NOT NULL,
  details TEXT
);

CREATE TABLE health_snapshots (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  component TEXT NOT NULL,
  status TEXT NOT NULL,
  message_ar TEXT,
  action TEXT,
  checked_at TEXT NOT NULL
);
CREATE INDEX ix_health_snapshots_component ON health_snapshots(component, checked_at);

-- ============================================================================
-- §2 — parties (synced)
-- ============================================================================

CREATE TABLE parties (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  kind TEXT NOT NULL,
  contact_name TEXT,
  phone TEXT,
  email TEXT,
  address TEXT,
  notes TEXT,
  x25519_pub BLOB,
  qr_token TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE party_names (
  id TEXT PRIMARY KEY,
  party_id TEXT NOT NULL REFERENCES parties(id),
  name TEXT NOT NULL,
  valid_from TEXT NOT NULL,
  valid_to TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_party_names_party_id ON party_names(party_id);

-- ============================================================================
-- §3 — correspondence and documents
-- ============================================================================

CREATE TABLE correspondence (
  id TEXT PRIMARY KEY,
  direction TEXT NOT NULL,
  official_number TEXT,
  number_issued_at TEXT,
  external_number TEXT,
  external_date TEXT,
  subject TEXT NOT NULL,
  type TEXT,
  confidentiality TEXT NOT NULL,
  recipient_only INTEGER NOT NULL DEFAULT 0,
  counterparty_kind TEXT NOT NULL,
  party_id TEXT REFERENCES parties(id),
  unit_id TEXT REFERENCES org_units(id),
  party_name_snapshot TEXT,
  cc TEXT,
  status TEXT NOT NULL,
  next_step_ar TEXT,
  due_at TEXT,
  linked_correspondence_id TEXT REFERENCES correspondence(id),
  case_id TEXT,
  meeting_id TEXT,
  template_id TEXT,
  body_text TEXT,
  approved_at TEXT,
  cancel_reason TEXT,
  close_note TEXT,
  archived_at TEXT,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
-- Deliberately NOT filtered on deleted_at: an official number must stay globally unique
-- forever, even against a soft-deleted correspondence row (numbers are never reissued).
CREATE UNIQUE INDEX ux_correspondence_official_number ON correspondence(official_number) WHERE official_number IS NOT NULL;
CREATE INDEX ix_correspondence_status ON correspondence(status);
CREATE INDEX ix_correspondence_party_id ON correspondence(party_id);
CREATE INDEX ix_correspondence_due_at ON correspondence(due_at);

CREATE TABLE correspondence_documents (
  id TEXT PRIMARY KEY,
  correspondence_id TEXT NOT NULL REFERENCES correspondence(id),
  document_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  sort INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_correspondence_documents_correspondence_id ON correspondence_documents(correspondence_id);
CREATE INDEX ix_correspondence_documents_document_id ON correspondence_documents(document_id);

CREATE TABLE documents (
  id TEXT PRIMARY KEY,
  sha256 TEXT NOT NULL,
  size INTEGER NOT NULL,
  mime TEXT NOT NULL,
  original_name TEXT NOT NULL,
  source TEXT NOT NULL,
  page_count INTEGER NOT NULL DEFAULT 0,
  ocr_status TEXT NOT NULL,
  ocr_lang TEXT,
  pinned_on_phone INTEGER NOT NULL DEFAULT 0,
  derived_from_id TEXT REFERENCES documents(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_documents_sha256 ON documents(sha256);

-- document_pages and document_links are official SYNCED tables (DATA-MODEL.md §3, decision of
-- 2026-09-16): OCR page text is produced on the phone or on one PC and must reach the other
-- devices without re-running OCR, and a document's entity links must travel with it. Both
-- therefore carry the full §0 column set with their own UUID v7 `id` and the AFTER
-- INSERT/UPDATE change_log triggers below; their natural keys become unfiltered unique indexes.
CREATE TABLE document_pages (
  id TEXT PRIMARY KEY,
  document_id TEXT NOT NULL REFERENCES documents(id),
  page_no INTEGER NOT NULL,
  text TEXT,
  words TEXT,
  confidence REAL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE UNIQUE INDEX ux_document_pages_document_id_page_no ON document_pages(document_id, page_no);

CREATE TABLE document_links (
  id TEXT PRIMARY KEY,
  document_id TEXT NOT NULL REFERENCES documents(id),
  entity_type TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE UNIQUE INDEX ux_document_links_document_id_entity ON document_links(document_id, entity_type, entity_id);
CREATE INDEX ix_document_links_entity ON document_links(entity_type, entity_id);

CREATE TABLE referrals (
  id TEXT PRIMARY KEY,
  correspondence_id TEXT NOT NULL REFERENCES correspondence(id),
  to_unit_id TEXT REFERENCES org_units(id),
  to_name TEXT,
  text TEXT NOT NULL,
  due_at TEXT,
  status TEXT NOT NULL,
  derived_document_id TEXT REFERENCES documents(id),
  extra_page_added INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_referrals_correspondence_id ON referrals(correspondence_id);

CREATE TABLE followups (
  id TEXT PRIMARY KEY,
  correspondence_id TEXT NOT NULL REFERENCES correspondence(id),
  kind TEXT NOT NULL,
  note TEXT,
  next_at TEXT,
  reminder_at TEXT,
  status_from TEXT,
  status_to TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_followups_correspondence_id ON followups(correspondence_id);

CREATE TABLE corrections (
  id TEXT PRIMARY KEY,
  correspondence_id TEXT NOT NULL REFERENCES correspondence(id),
  changes TEXT NOT NULL,
  reason TEXT NOT NULL,
  at TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_corrections_correspondence_id ON corrections(correspondence_id);

CREATE TABLE duplicate_reviews (
  id TEXT PRIMARY KEY,
  correspondence_id TEXT NOT NULL REFERENCES correspondence(id),
  similar_id TEXT NOT NULL REFERENCES correspondence(id),
  score REAL NOT NULL,
  verdict TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE templates (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  document_id TEXT REFERENCES documents(id),
  fields TEXT,
  is_default INTEGER NOT NULL DEFAULT 0,
  kind TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE exchange_log (
  id TEXT PRIMARY KEY,
  direction TEXT NOT NULL,
  kind TEXT NOT NULL,
  file_name TEXT NOT NULL,
  other_org TEXT,
  other_office TEXT,
  entity_id TEXT,
  signature_ok INTEGER NOT NULL DEFAULT 0,
  at TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

-- ============================================================================
-- §4 — follow-up and tasks (synced)
-- ============================================================================

CREATE TABLE tasks (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  description TEXT,
  due_at TEXT,
  priority TEXT NOT NULL,
  status TEXT NOT NULL,
  progress INTEGER NOT NULL DEFAULT 0,
  assignee_name TEXT,
  source_type TEXT,
  source_id TEXT,
  reminder_at TEXT,
  postpone_reason TEXT,
  completed_at TEXT,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  source_device_kind TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_tasks_status ON tasks(status);
CREATE INDEX ix_tasks_due_at ON tasks(due_at);
CREATE INDEX ix_tasks_source ON tasks(source_type, source_id);

CREATE TABLE decisions (
  id TEXT PRIMARY KEY,
  text TEXT NOT NULL,
  source_type TEXT NOT NULL,
  source_id TEXT,
  decided_at TEXT NOT NULL,
  owner_name TEXT,
  status TEXT NOT NULL,
  execution_pct INTEGER NOT NULL DEFAULT 0,
  due_at TEXT,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_decisions_source ON decisions(source_type, source_id);

CREATE TABLE commitments (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  party_id TEXT REFERENCES parties(id),
  amount INTEGER NOT NULL,
  required_text TEXT,
  due_at TEXT,
  status TEXT NOT NULL,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_commitments_status ON commitments(status);

CREATE TABLE commitment_payments (
  id TEXT PRIMARY KEY,
  commitment_id TEXT NOT NULL REFERENCES commitments(id),
  amount INTEGER NOT NULL,
  paid_at TEXT NOT NULL,
  note TEXT,
  transaction_id TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_commitment_payments_commitment_id ON commitment_payments(commitment_id);

CREATE TABLE obstacles (
  id TEXT PRIMARY KEY,
  description TEXT NOT NULL,
  impact TEXT,
  required_from_parent TEXT,
  status TEXT NOT NULL,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE needs (
  id TEXT PRIMARY KEY,
  item TEXT NOT NULL,
  justification TEXT,
  priority TEXT,
  status TEXT,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE notes (
  id TEXT PRIMARY KEY,
  text TEXT NOT NULL,
  voice_document_id TEXT REFERENCES documents(id),
  entity_type TEXT,
  entity_id TEXT,
  for_report INTEGER NOT NULL DEFAULT 0,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  source_device_kind TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_notes_entity ON notes(entity_type, entity_id);

-- ============================================================================
-- §5 — meetings and calendar (synced)
-- ============================================================================

CREATE TABLE meetings (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  starts_at TEXT NOT NULL,
  duration_min INTEGER NOT NULL DEFAULT 0,
  location TEXT,
  agenda TEXT,
  minutes_text TEXT,
  status TEXT NOT NULL,
  reminder_minutes INTEGER,
  correspondence_id TEXT REFERENCES correspondence(id),
  case_id TEXT,
  report_include INTEGER NOT NULL DEFAULT 0,
  report_highlight INTEGER NOT NULL DEFAULT 0,
  report_comment TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_meetings_starts_at ON meetings(starts_at);

CREATE TABLE meeting_attendees (
  id TEXT PRIMARY KEY,
  meeting_id TEXT NOT NULL REFERENCES meetings(id),
  kind TEXT NOT NULL,
  ref_id TEXT,
  name TEXT NOT NULL,
  role TEXT NOT NULL,
  attended INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_meeting_attendees_meeting_id ON meeting_attendees(meeting_id);

CREATE TABLE appointments (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  starts_at TEXT NOT NULL,
  ends_at TEXT,
  notes TEXT,
  reminder_minutes INTEGER,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_appointments_starts_at ON appointments(starts_at);

-- ============================================================================
-- §6 — cases (synced)
-- ============================================================================

CREATE TABLE cases (
  id TEXT PRIMARY KEY,
  case_number TEXT NOT NULL,
  title TEXT NOT NULL,
  party_id TEXT REFERENCES parties(id),
  stage TEXT,
  next_hearing_at TEXT,
  responsible_name TEXT,
  status TEXT NOT NULL,
  notes TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE case_events (
  id TEXT PRIMARY KEY,
  case_id TEXT NOT NULL REFERENCES cases(id),
  at TEXT NOT NULL,
  kind TEXT NOT NULL,
  description TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_case_events_case_id ON case_events(case_id);

CREATE TABLE case_parties (
  id TEXT PRIMARY KEY,
  case_id TEXT NOT NULL REFERENCES cases(id),
  party_id TEXT NOT NULL REFERENCES parties(id),
  role TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_case_parties_case_id ON case_parties(case_id);

-- ============================================================================
-- §7 — employees and payroll (synced)
-- ============================================================================

CREATE TABLE employees (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  employee_number TEXT NOT NULL,
  unit_id TEXT REFERENCES org_units(id),
  job_title TEXT,
  status TEXT NOT NULL,
  hired_at TEXT,
  terminated_at TEXT,
  phone TEXT,
  photo_document_id TEXT REFERENCES documents(id),
  notes TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE salary_components (
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL REFERENCES employees(id),
  kind TEXT NOT NULL,
  name TEXT NOT NULL,
  amount INTEGER NOT NULL,
  valid_from TEXT NOT NULL,
  valid_to TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_salary_components_employee_id ON salary_components(employee_id);

CREATE TABLE payroll_runs (
  id TEXT PRIMARY KEY,
  period TEXT NOT NULL,
  unit_id TEXT REFERENCES org_units(id),
  status TEXT NOT NULL,
  committed_at TEXT,
  totals TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE payroll_lines (
  id TEXT PRIMARY KEY,
  run_id TEXT NOT NULL REFERENCES payroll_runs(id),
  employee_id TEXT NOT NULL REFERENCES employees(id),
  basic INTEGER NOT NULL DEFAULT 0,
  allowances INTEGER NOT NULL DEFAULT 0,
  deductions INTEGER NOT NULL DEFAULT 0,
  bonuses INTEGER NOT NULL DEFAULT 0,
  net INTEGER NOT NULL DEFAULT 0,
  overrides TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_payroll_lines_run_id ON payroll_lines(run_id);

CREATE TABLE bonuses (
  id TEXT PRIMARY KEY,
  employee_id TEXT NOT NULL REFERENCES employees(id),
  type TEXT NOT NULL,
  amount INTEGER NOT NULL,
  reason TEXT,
  granted_at TEXT NOT NULL,
  run_id TEXT REFERENCES payroll_runs(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE payroll_imports (
  id TEXT PRIMARY KEY,
  file_name TEXT NOT NULL,
  unit_id TEXT REFERENCES org_units(id),
  period TEXT NOT NULL,
  totals TEXT,
  signature_ok INTEGER NOT NULL DEFAULT 0,
  signer_device TEXT,
  imported_at TEXT NOT NULL,
  run_id TEXT REFERENCES payroll_runs(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

-- ============================================================================
-- §8 — assets and custody (synced)
-- ============================================================================

CREATE TABLE assets (
  id TEXT PRIMARY KEY,
  inventory_number TEXT NOT NULL,
  name TEXT NOT NULL,
  category TEXT,
  status TEXT NOT NULL,
  location TEXT,
  custodian_employee_id TEXT REFERENCES employees(id),
  acquired_at TEXT,
  value INTEGER,
  notes TEXT,
  qr_token TEXT,
  origin_office_code TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
-- Unfiltered per the unique-index policy at the top of this file: an inventory number
-- (<office_code>-<seq>, AGREEMENT item 45) must stay unique forever, even against a
-- soft-deleted asset, so custody history stays intact and no sync collision is possible.
CREATE UNIQUE INDEX ux_assets_inventory_number ON assets(inventory_number);

CREATE TABLE custody_movements (
  id TEXT PRIMARY KEY,
  asset_id TEXT NOT NULL REFERENCES assets(id),
  kind TEXT NOT NULL,
  from_employee_id TEXT REFERENCES employees(id),
  to_employee_id TEXT REFERENCES employees(id),
  at TEXT NOT NULL,
  note TEXT,
  confirmed_at TEXT,
  receipt_document_id TEXT REFERENCES documents(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_custody_movements_asset_id ON custody_movements(asset_id);

CREATE TABLE asset_transfers (
  id TEXT PRIMARY KEY,
  direction TEXT NOT NULL,
  other_office_unit_id TEXT REFERENCES org_units(id),
  asset_ids TEXT NOT NULL,
  status TEXT NOT NULL,
  package_file TEXT,
  at TEXT NOT NULL,
  decided_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE inventory_sessions (
  id TEXT PRIMARY KEY,
  started_at TEXT NOT NULL,
  ended_at TEXT,
  status TEXT,
  exported_file TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE inventory_items (
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL REFERENCES inventory_sessions(id),
  asset_id TEXT NOT NULL REFERENCES assets(id),
  result TEXT NOT NULL,
  checked_at TEXT NOT NULL,
  via TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_inventory_items_session_id ON inventory_items(session_id);

-- ============================================================================
-- §9 — finance (synced)
-- ============================================================================

CREATE TABLE financial_cycles (
  id TEXT PRIMARY KEY,
  name_ar TEXT NOT NULL,
  start_date TEXT NOT NULL,
  end_date TEXT NOT NULL,
  status TEXT NOT NULL,
  issued_at TEXT,
  report_id TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
-- Unfiltered per the unique-index policy at the top of this file: one cycle per start date,
-- soft-deleted rows included. FinancialCycleService looks the start date up with
-- IgnoreDeleted() and restores a hidden row instead of inserting a second one.
CREATE UNIQUE INDEX ux_financial_cycles_start_date ON financial_cycles(start_date);

CREATE TABLE categories (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  name TEXT NOT NULL,
  sort INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

CREATE TABLE transactions (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  amount INTEGER NOT NULL,
  purpose TEXT NOT NULL,
  category_id TEXT REFERENCES categories(id),
  at TEXT NOT NULL,
  source TEXT NOT NULL,
  receipt_document_id TEXT REFERENCES documents(id),
  note TEXT,
  cycle_id TEXT REFERENCES financial_cycles(id),
  phone_expense_id TEXT,
  correction_of_id TEXT REFERENCES transactions(id),
  original_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_transactions_cycle_id ON transactions(cycle_id);
CREATE INDEX ix_transactions_at ON transactions(at);

CREATE TABLE ledger_entries (
  id TEXT PRIMARY KEY,
  transaction_id TEXT NOT NULL REFERENCES transactions(id),
  account TEXT NOT NULL,
  debit INTEGER NOT NULL DEFAULT 0,
  credit INTEGER NOT NULL DEFAULT 0,
  at TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_ledger_entries_transaction_id ON ledger_entries(transaction_id);

CREATE TABLE phone_expenses (
  id TEXT PRIMARY KEY,
  phone_device_id TEXT NOT NULL REFERENCES devices(id),
  amount INTEGER NOT NULL,
  purpose TEXT NOT NULL,
  category_name TEXT,
  at TEXT NOT NULL,
  receipt_document_id TEXT REFERENCES documents(id),
  note TEXT,
  status TEXT NOT NULL,
  reject_reason TEXT,
  transaction_id TEXT REFERENCES transactions(id),
  decided_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_phone_expenses_status ON phone_expenses(status);

CREATE TABLE cash_counts (
  id TEXT PRIMARY KEY,
  at TEXT NOT NULL,
  book_balance INTEGER NOT NULL,
  counted TEXT NOT NULL,
  counted_total INTEGER NOT NULL,
  difference INTEGER NOT NULL,
  note TEXT,
  cycle_id TEXT REFERENCES financial_cycles(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

-- ============================================================================
-- §10 — reports (synced)
-- ============================================================================

CREATE TABLE monthly_reports (
  id TEXT PRIMARY KEY,
  cycle_id TEXT NOT NULL REFERENCES financial_cycles(id),
  status TEXT NOT NULL,
  sections TEXT,
  readiness TEXT,
  director_word TEXT,
  issued_at TEXT,
  docx_document_id TEXT REFERENCES documents(id),
  pdf_document_id TEXT REFERENCES documents(id),
  sha256 TEXT,
  outgoing_correspondence_id TEXT REFERENCES correspondence(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
-- Unfiltered per the unique-index policy at the top of this file: one monthly report per cycle,
-- soft-deleted rows included; B5 looks the cycle up with IgnoreDeleted() and handles a hidden
-- report explicitly.
CREATE UNIQUE INDEX ux_monthly_reports_cycle_id ON monthly_reports(cycle_id);

CREATE TABLE report_addenda (
  id TEXT PRIMARY KEY,
  report_id TEXT NOT NULL REFERENCES monthly_reports(id),
  number INTEGER NOT NULL,
  reason TEXT NOT NULL,
  item TEXT NOT NULL,
  text TEXT NOT NULL,
  issued_at TEXT NOT NULL,
  document_id TEXT REFERENCES documents(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);
CREATE INDEX ix_report_addenda_report_id ON report_addenda(report_id);

CREATE TABLE other_reports (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL,
  filters TEXT,
  generated_at TEXT NOT NULL,
  document_id TEXT REFERENCES documents(id),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  origin_device TEXT NOT NULL,
  row_version INTEGER NOT NULL DEFAULT 0,
  base_version INTEGER NOT NULL DEFAULT 0,
  deleted_at TEXT
);

-- ============================================================================
-- §11 — sync, backup and phone bookkeeping. Local tables (not synced).
-- ============================================================================

CREATE TABLE change_log (
  seq INTEGER PRIMARY KEY AUTOINCREMENT,
  table_name TEXT NOT NULL,
  row_id TEXT NOT NULL,
  op TEXT NOT NULL,
  at TEXT NOT NULL,
  device TEXT NOT NULL
);
CREATE INDEX ix_change_log_table_row ON change_log(table_name, row_id);
CREATE INDEX ix_change_log_at ON change_log(at);

CREATE TABLE sync_packages (
  id TEXT PRIMARY KEY,
  direction TEXT NOT NULL,
  kind TEXT NOT NULL,
  from_date TEXT,
  to_date TEXT,
  target_device_id TEXT REFERENCES devices(id),
  source_device_id TEXT REFERENCES devices(id),
  file_name TEXT NOT NULL,
  counts TEXT,
  status TEXT NOT NULL,
  created_at TEXT NOT NULL,
  applied_at TEXT
);

CREATE TABLE sync_conflicts (
  id TEXT PRIMARY KEY,
  package_id TEXT NOT NULL REFERENCES sync_packages(id),
  table_name TEXT NOT NULL,
  row_id TEXT NOT NULL,
  ours TEXT NOT NULL,
  theirs TEXT NOT NULL,
  resolution TEXT,
  resolved_at TEXT
);
CREATE INDEX ix_sync_conflicts_package_id ON sync_conflicts(package_id);

CREATE TABLE phone_queue (
  id TEXT PRIMARY KEY,
  direction TEXT NOT NULL,
  seq INTEGER NOT NULL,
  file_name TEXT NOT NULL,
  status TEXT NOT NULL,
  at TEXT NOT NULL,
  items TEXT
);

CREATE TABLE pairing_sessions (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL,
  short_code_hash TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  status TEXT NOT NULL,
  phone_device_id TEXT REFERENCES devices(id)
);

CREATE TABLE pinned_files (
  document_id TEXT PRIMARY KEY REFERENCES documents(id),
  pinned_at TEXT NOT NULL,
  sent_at TEXT
);

CREATE TABLE backups (
  id TEXT PRIMARY KEY,
  at TEXT NOT NULL,
  file_path TEXT NOT NULL,
  size INTEGER NOT NULL,
  includes_vault INTEGER NOT NULL DEFAULT 0,
  app_version TEXT NOT NULL
);

CREATE TABLE restore_log (
  id TEXT PRIMARY KEY,
  at TEXT NOT NULL,
  file_path TEXT NOT NULL,
  backup_at TEXT NOT NULL,
  sequence_check TEXT NOT NULL,
  details TEXT
);

-- ============================================================================
-- §12 — search and models. Local tables (not synced).
-- ============================================================================

CREATE TABLE search_chunks (
  id TEXT PRIMARY KEY,
  entity_type TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  document_id TEXT REFERENCES documents(id),
  page_no INTEGER,
  text_norm TEXT NOT NULL,
  embedding BLOB,
  model_id TEXT,
  updated_at TEXT NOT NULL
);
CREATE INDEX ix_search_chunks_entity ON search_chunks(entity_type, entity_id);

CREATE TABLE models (
  id TEXT PRIMARY KEY,
  path TEXT NOT NULL,
  kind TEXT NOT NULL,
  name TEXT NOT NULL,
  status TEXT NOT NULL,
  reason_ar TEXT,
  dims INTEGER,
  checked_at TEXT NOT NULL
);

-- FTS5 virtual table kept in sync with search_chunks by triggers below. Queried with raw SQL
-- (MATCH), not through EF — FTS5 virtual tables have no natural EF Core shape.
CREATE VIRTUAL TABLE search_fts USING fts5(
  text_norm,
  content='search_chunks',
  content_rowid='rowid',
  tokenize='unicode61 remove_diacritics 2'
);

CREATE TRIGGER trg_search_chunks_ai AFTER INSERT ON search_chunks BEGIN
  INSERT INTO search_fts(rowid, text_norm) VALUES (new.rowid, new.text_norm);
END;

CREATE TRIGGER trg_search_chunks_ad AFTER DELETE ON search_chunks BEGIN
  INSERT INTO search_fts(search_fts, rowid, text_norm) VALUES ('delete', old.rowid, old.text_norm);
END;

CREATE TRIGGER trg_search_chunks_au AFTER UPDATE ON search_chunks BEGIN
  INSERT INTO search_fts(search_fts, rowid, text_norm) VALUES ('delete', old.rowid, old.text_norm);
  INSERT INTO search_fts(rowid, text_norm) VALUES (new.rowid, new.text_norm);
END;

-- ============================================================================
-- change_log triggers — one AFTER INSERT and one AFTER UPDATE per synced table
-- (DATA-MODEL.md §0 / ARCHITECTURE.md §6). Local/technical tables are excluded.
-- The `device` column records the device that performed the operation locally
-- (installation.device_id) rather than the row's own origin_device — confirmed in
-- DATA-MODEL.md §11 on 2026-09-16 — so a date-range export selects what changed here. On
-- INSERT the two are the same value for a locally created row, and for a row applied by the
-- sync-import layer the inserted origin_device is deliberately kept so the import is
-- attributed to the device that actually created the row. There is no AFTER DELETE trigger
-- because official rows are never physically deleted: WakeelDb turns every Remove() into a
-- soft delete, which fires the AFTER UPDATE trigger above ('U').
-- ============================================================================

CREATE TRIGGER trg_parties_ai AFTER INSERT ON parties BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('parties', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_parties_au AFTER UPDATE ON parties BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('parties', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_party_names_ai AFTER INSERT ON party_names BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('party_names', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_party_names_au AFTER UPDATE ON party_names BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('party_names', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_correspondence_ai AFTER INSERT ON correspondence BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('correspondence', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_correspondence_au AFTER UPDATE ON correspondence BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('correspondence', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_correspondence_documents_ai AFTER INSERT ON correspondence_documents BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('correspondence_documents', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_correspondence_documents_au AFTER UPDATE ON correspondence_documents BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('correspondence_documents', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_documents_ai AFTER INSERT ON documents BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('documents', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_documents_au AFTER UPDATE ON documents BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('documents', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_document_pages_ai AFTER INSERT ON document_pages BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('document_pages', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_document_pages_au AFTER UPDATE ON document_pages BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('document_pages', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_document_links_ai AFTER INSERT ON document_links BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('document_links', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_document_links_au AFTER UPDATE ON document_links BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('document_links', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_referrals_ai AFTER INSERT ON referrals BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('referrals', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_referrals_au AFTER UPDATE ON referrals BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('referrals', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_followups_ai AFTER INSERT ON followups BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('followups', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_followups_au AFTER UPDATE ON followups BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('followups', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_corrections_ai AFTER INSERT ON corrections BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('corrections', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_corrections_au AFTER UPDATE ON corrections BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('corrections', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_duplicate_reviews_ai AFTER INSERT ON duplicate_reviews BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('duplicate_reviews', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_duplicate_reviews_au AFTER UPDATE ON duplicate_reviews BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('duplicate_reviews', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_templates_ai AFTER INSERT ON templates BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('templates', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_templates_au AFTER UPDATE ON templates BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('templates', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_exchange_log_ai AFTER INSERT ON exchange_log BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('exchange_log', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_exchange_log_au AFTER UPDATE ON exchange_log BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('exchange_log', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_tasks_ai AFTER INSERT ON tasks BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('tasks', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_tasks_au AFTER UPDATE ON tasks BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('tasks', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_decisions_ai AFTER INSERT ON decisions BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('decisions', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_decisions_au AFTER UPDATE ON decisions BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('decisions', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_commitments_ai AFTER INSERT ON commitments BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('commitments', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_commitments_au AFTER UPDATE ON commitments BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('commitments', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_commitment_payments_ai AFTER INSERT ON commitment_payments BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('commitment_payments', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_commitment_payments_au AFTER UPDATE ON commitment_payments BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('commitment_payments', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_obstacles_ai AFTER INSERT ON obstacles BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('obstacles', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_obstacles_au AFTER UPDATE ON obstacles BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('obstacles', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_needs_ai AFTER INSERT ON needs BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('needs', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_needs_au AFTER UPDATE ON needs BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('needs', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_notes_ai AFTER INSERT ON notes BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('notes', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_notes_au AFTER UPDATE ON notes BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('notes', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_meetings_ai AFTER INSERT ON meetings BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('meetings', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_meetings_au AFTER UPDATE ON meetings BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('meetings', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_meeting_attendees_ai AFTER INSERT ON meeting_attendees BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('meeting_attendees', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_meeting_attendees_au AFTER UPDATE ON meeting_attendees BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('meeting_attendees', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_appointments_ai AFTER INSERT ON appointments BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('appointments', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_appointments_au AFTER UPDATE ON appointments BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('appointments', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_cases_ai AFTER INSERT ON cases BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('cases', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_cases_au AFTER UPDATE ON cases BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('cases', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_case_events_ai AFTER INSERT ON case_events BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('case_events', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_case_events_au AFTER UPDATE ON case_events BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('case_events', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_case_parties_ai AFTER INSERT ON case_parties BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('case_parties', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_case_parties_au AFTER UPDATE ON case_parties BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('case_parties', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_employees_ai AFTER INSERT ON employees BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('employees', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_employees_au AFTER UPDATE ON employees BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('employees', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_salary_components_ai AFTER INSERT ON salary_components BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('salary_components', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_salary_components_au AFTER UPDATE ON salary_components BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('salary_components', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_payroll_runs_ai AFTER INSERT ON payroll_runs BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('payroll_runs', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_payroll_runs_au AFTER UPDATE ON payroll_runs BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('payroll_runs', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_payroll_lines_ai AFTER INSERT ON payroll_lines BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('payroll_lines', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_payroll_lines_au AFTER UPDATE ON payroll_lines BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('payroll_lines', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_bonuses_ai AFTER INSERT ON bonuses BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('bonuses', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_bonuses_au AFTER UPDATE ON bonuses BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('bonuses', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_payroll_imports_ai AFTER INSERT ON payroll_imports BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('payroll_imports', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_payroll_imports_au AFTER UPDATE ON payroll_imports BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('payroll_imports', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_assets_ai AFTER INSERT ON assets BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('assets', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_assets_au AFTER UPDATE ON assets BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('assets', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_custody_movements_ai AFTER INSERT ON custody_movements BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('custody_movements', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_custody_movements_au AFTER UPDATE ON custody_movements BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('custody_movements', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_asset_transfers_ai AFTER INSERT ON asset_transfers BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('asset_transfers', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_asset_transfers_au AFTER UPDATE ON asset_transfers BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('asset_transfers', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_inventory_sessions_ai AFTER INSERT ON inventory_sessions BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('inventory_sessions', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_inventory_sessions_au AFTER UPDATE ON inventory_sessions BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('inventory_sessions', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_inventory_items_ai AFTER INSERT ON inventory_items BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('inventory_items', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_inventory_items_au AFTER UPDATE ON inventory_items BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('inventory_items', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_financial_cycles_ai AFTER INSERT ON financial_cycles BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('financial_cycles', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_financial_cycles_au AFTER UPDATE ON financial_cycles BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('financial_cycles', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_categories_ai AFTER INSERT ON categories BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('categories', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_categories_au AFTER UPDATE ON categories BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('categories', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_transactions_ai AFTER INSERT ON transactions BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('transactions', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_transactions_au AFTER UPDATE ON transactions BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('transactions', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_ledger_entries_ai AFTER INSERT ON ledger_entries BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('ledger_entries', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_ledger_entries_au AFTER UPDATE ON ledger_entries BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('ledger_entries', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_phone_expenses_ai AFTER INSERT ON phone_expenses BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('phone_expenses', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_phone_expenses_au AFTER UPDATE ON phone_expenses BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('phone_expenses', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_cash_counts_ai AFTER INSERT ON cash_counts BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('cash_counts', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_cash_counts_au AFTER UPDATE ON cash_counts BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('cash_counts', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_monthly_reports_ai AFTER INSERT ON monthly_reports BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('monthly_reports', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_monthly_reports_au AFTER UPDATE ON monthly_reports BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('monthly_reports', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_report_addenda_ai AFTER INSERT ON report_addenda BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('report_addenda', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_report_addenda_au AFTER UPDATE ON report_addenda BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('report_addenda', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;

CREATE TRIGGER trg_other_reports_ai AFTER INSERT ON other_reports BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('other_reports', NEW.id, 'I', NEW.created_at, NEW.origin_device); END;
CREATE TRIGGER trg_other_reports_au AFTER UPDATE ON other_reports BEGIN INSERT INTO change_log(table_name,row_id,op,at,device) VALUES ('other_reports', NEW.id, 'U', NEW.updated_at, COALESCE((SELECT device_id FROM installation LIMIT 1), NEW.origin_device)); END;
