-- admin.db — the administration tool's own database (DATA-MODEL.md §13).
--
-- It is not a الوكيل installation database: nothing here is synchronised, nothing carries the
-- shared columns of §0, and there are no change-log triggers. The tool is the single writer.
--
-- Every secret stored here (the organisation private seeds, an office key, a device key seed) is
-- sealed with the database key before it is written, so a row read out of a stolen file is still
-- nothing but ciphertext.

-- The organisation itself: one row, created at A01 together with the organisation keys.
CREATE TABLE org (
    id                   TEXT PRIMARY KEY,
    name                 TEXT NOT NULL,
    logo                 BLOB,
    logo_mime            TEXT,
    cycle_start_day      INTEGER NOT NULL DEFAULT 1 CHECK (cycle_start_day BETWEEN 1 AND 28),
    numbering_format     TEXT NOT NULL DEFAULT 'YYYYMMDD/DESSS',
    report_template      BLOB,
    report_template_name TEXT,
    letter_template      BLOB,
    letter_template_name TEXT,
    ed25519_pub          TEXT NOT NULL,
    x25519_pub           TEXT NOT NULL,
    sealed_seeds         BLOB NOT NULL,
    root_certificate     TEXT NOT NULL,
    created_at           TEXT NOT NULL,
    updated_at           TEXT NOT NULL
);

-- The administrator account. The password wraps and the attempt counter live in admin.key, which
-- has to be readable before this database can be opened at all; this row is the part that can only
-- be read once the password has already worked.
CREATE TABLE admin_account (
    id                  INTEGER PRIMARY KEY CHECK (id = 1),
    display_name        TEXT NOT NULL,
    password_changed_at TEXT NOT NULL,
    created_at          TEXT NOT NULL
);

-- The four fixed layers: هيئة ← دائرة ← قسم ← وحدة.
CREATE TABLE org_units (
    id          TEXT PRIMARY KEY,
    parent_id   TEXT REFERENCES org_units(id),
    level       TEXT NOT NULL CHECK (level IN ('org', 'department', 'section', 'unit')),
    name        TEXT NOT NULL,
    head_name   TEXT,
    head_title  TEXT,
    office_code TEXT,
    sort_order  INTEGER NOT NULL DEFAULT 0,
    x25519_pub  TEXT,
    disabled_at TEXT,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE INDEX ix_org_units_parent ON org_units(parent_id);

-- No two children of the same parent may carry the same name (A05's «لا تكرار في المستوى نفسه»).
CREATE UNIQUE INDEX ux_org_units_sibling_name ON org_units(COALESCE(parent_id, ''), name);

-- An office is a unit that has a computer in it.
CREATE TABLE offices (
    id           TEXT PRIMARY KEY,
    unit_id      TEXT NOT NULL REFERENCES org_units(id),
    name         TEXT NOT NULL,
    office_code  TEXT NOT NULL,
    sealed_key   BLOB,
    key_version  INTEGER NOT NULL DEFAULT 0,
    key_rotated_at TEXT,
    activated_at TEXT,
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_offices_code ON offices(office_code);
CREATE INDEX ix_offices_unit ON offices(unit_id);

-- Every computer and paired phone the organisation knows about.
CREATE TABLE devices (
    id                TEXT PRIMARY KEY,
    office_id         TEXT NOT NULL REFERENCES offices(id),
    device_no         INTEGER NOT NULL CHECK (device_no BETWEEN 1 AND 9),
    kind              TEXT NOT NULL DEFAULT 'pc' CHECK (kind IN ('pc', 'phone')),
    ed25519_pub       TEXT NOT NULL,
    x25519_pub        TEXT NOT NULL,
    certificate       TEXT,
    sealed_seeds      BLOB,
    seeds_exported_at TEXT,
    issued_at         TEXT NOT NULL,
    revoked_at        TEXT,
    paired_at         TEXT,
    last_sync_at      TEXT,
    created_at        TEXT NOT NULL,
    updated_at        TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_devices_office_no ON devices(office_id, device_no);
CREATE INDEX ix_devices_office ON devices(office_id);

-- Who sits at each device, and with which of the three roles.
CREATE TABLE accounts (
    id            TEXT PRIMARY KEY,
    device_id     TEXT NOT NULL REFERENCES devices(id),
    employee_name TEXT NOT NULL,
    employee_no   INTEGER NOT NULL CHECK (employee_no BETWEEN 1 AND 9),
    role          TEXT NOT NULL CHECK (role IN ('manager', 'secretary', 'custodian')),
    sync_scope    TEXT NOT NULL DEFAULT 'full' CHECK (sync_scope IN ('full', 'custody')),
    status        TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'active', 'revoked')),
    created_at    TEXT NOT NULL,
    updated_at    TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_accounts_device ON accounts(device_id);

-- Every setup file that has ever left the tool.
CREATE TABLE setup_exports (
    id          TEXT PRIMARY KEY,
    device_id   TEXT NOT NULL REFERENCES devices(id),
    version     INTEGER NOT NULL,
    exported_at TEXT NOT NULL,
    file_name   TEXT NOT NULL,
    includes    TEXT NOT NULL
);

CREATE INDEX ix_setup_exports_device ON setup_exports(device_id);
CREATE INDEX ix_setup_exports_at ON setup_exports(exported_at);

-- Structure edits that no office has been told about yet (A10).
CREATE TABLE pending_changes (
    id             TEXT PRIMARY KEY,
    entity_type    TEXT NOT NULL,
    entity_id      TEXT NOT NULL,
    summary_ar     TEXT NOT NULL,
    created_at     TEXT NOT NULL,
    distributed_at TEXT
);

CREATE INDEX ix_pending_changes_open ON pending_changes(distributed_at);

-- Append only, and never a secret in it (A11).
CREATE TABLE audit_log (
    id          TEXT PRIMARY KEY,
    at          TEXT NOT NULL,
    actor       TEXT NOT NULL,
    action      TEXT NOT NULL,
    entity_type TEXT,
    entity_id   TEXT,
    summary_ar  TEXT NOT NULL,
    details     TEXT
);

CREATE INDEX ix_audit_log_at ON audit_log(at);
