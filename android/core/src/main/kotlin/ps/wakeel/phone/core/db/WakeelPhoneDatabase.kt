package ps.wakeel.phone.core.db

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory
import ps.wakeel.phone.core.crypto.KeyVault
import ps.wakeel.phone.core.crypto.KeyVaultLabels
import java.io.File

/**
 * The phone database of `docs/build/DATA-MODEL.md` §14. On the phone it is opened through
 * SQLCipher with a key that never exists outside the Android Keystore wrap; in a test it is
 * opened in memory, because the SQLCipher library is native to the device.
 */
@Database(
    entities = [
        CorrespondenceSummaryRow::class,
        TaskRow::class,
        MeetingRow::class,
        AppointmentRow::class,
        DecisionRow::class,
        CommitmentRow::class,
        ContactRow::class,
        NotificationRow::class,
        PhoneExpenseRow::class,
        CaptureRow::class,
        NoteRow::class,
        PinnedFileRow::class,
        SettingRow::class,
        OutboxRow::class,
        SyncStateRow::class,
    ],
    version = 1,
    exportSchema = true,
)
abstract class WakeelPhoneDatabase : RoomDatabase() {

    abstract fun correspondence(): CorrespondenceDao
    abstract fun tasks(): TaskDao
    abstract fun meetings(): MeetingDao
    abstract fun appointments(): AppointmentDao
    abstract fun decisions(): DecisionDao
    abstract fun commitments(): CommitmentDao
    abstract fun contacts(): ContactDao
    abstract fun notifications(): NotificationDao
    abstract fun phoneExpenses(): PhoneExpenseDao
    abstract fun captures(): CaptureDao
    abstract fun notes(): NoteDao
    abstract fun pinnedFiles(): PinnedFileDao
    abstract fun settings(): SettingDao
    abstract fun outbox(): OutboxDao
    abstract fun syncState(): SyncStateDao

    companion object {
        const val FILE_NAME = "wakeel-phone.db"

        /** The tables a packet from the computer may fill, in the order they are applied. */
        val MIRRORED_TABLES: List<String> = listOf(
            "contacts",
            "correspondence_summary",
            "tasks",
            "meetings",
            "appointments",
            "decisions",
            "commitments",
            "notifications",
            "phone_expenses",
            "notes",
            "pinned_files",
            "settings",
        )

        /**
         * Opens the real database. The key is thirty two random bytes made once on this phone
         * and kept only as a Keystore wrap; it is unwrapped into a byte array that is cleared
         * as soon as SQLCipher has taken it.
         */
        fun open(context: Context, vault: KeyVault, wrappedKey: ByteArray): WakeelPhoneDatabase {
            System.loadLibrary("sqlcipher")
            val key = vault.unwrap(wrappedKey, KeyVaultLabels.DATABASE_KEY)
            try {
                return Room.databaseBuilder(context, WakeelPhoneDatabase::class.java, FILE_NAME)
                    .openHelperFactory(SupportOpenHelperFactory(key))
                    .build()
            } finally {
                key.fill(0)
            }
        }

        /** Makes the database key for a phone that has none yet, and returns it wrapped. */
        fun newWrappedKey(vault: KeyVault): ByteArray =
            vault.wrap(vault.newSecret(32), KeyVaultLabels.DATABASE_KEY)

        /** The database in memory, for the tests. */
        fun inMemory(context: Context): WakeelPhoneDatabase =
            Room.inMemoryDatabaseBuilder(context, WakeelPhoneDatabase::class.java)
                .allowMainThreadQueries()
                .build()

        /** Where the application keeps the files that belong to its own records. */
        fun privateFileDirectory(context: Context): File =
            File(context.filesDir, "wakeel").apply { mkdirs() }
    }
}
