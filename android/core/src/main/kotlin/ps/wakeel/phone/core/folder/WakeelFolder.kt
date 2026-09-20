package ps.wakeel.phone.core.folder

import android.content.Context
import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import java.io.File
import java.io.InputStream
import java.io.OutputStream

/** One file seen inside the exchange folder. */
data class WakeelFileInfo(val name: String, val size: Long, val lastModified: Long)

/**
 * The `Wakeel` folder on the phone's shared storage, as the application sees it. The office
 * computer writes and reads the same folder over the cable; nothing else is exchanged.
 *
 * The interface exists because the application reaches the folder through the system's own
 * folder picker while the tests reach an ordinary directory, and the sync engine must not care
 * which of the two it is holding.
 */
interface WakeelFolder {
    fun list(): List<WakeelFileInfo>

    fun exists(name: String): Boolean

    fun openRead(name: String): InputStream

    /**
     * Writes a file so that it either appears complete or does not appear at all: the bytes go
     * to a neighbouring `.part` name and are renamed into place at the end. An interruption —
     * the cable pulled out, the application stopped — leaves only the partial name behind, and
     * [list] never reports one, so the far side can simply write it again.
     */
    fun write(name: String, body: (OutputStream) -> Unit)

    fun delete(name: String)

    /** Removes leftover partial files, which is what a run does before it starts writing. */
    fun clearPartials()

    companion object {
        /** The suffix a file carries while it is still being written. */
        const val PARTIAL_SUFFIX = ".part"
    }
}

/** The folder as an ordinary directory: how the tests and the interoperability vectors use it. */
class FileWakeelFolder(private val directory: File) : WakeelFolder {

    init {
        directory.mkdirs()
    }

    override fun list(): List<WakeelFileInfo> =
        directory.listFiles().orEmpty()
            .filter { it.isFile && !it.name.endsWith(WakeelFolder.PARTIAL_SUFFIX) }
            .map { WakeelFileInfo(it.name, it.length(), it.lastModified()) }
            .sortedBy { it.name }

    override fun exists(name: String): Boolean = File(directory, name).isFile

    override fun openRead(name: String): InputStream = File(directory, name).inputStream()

    override fun write(name: String, body: (OutputStream) -> Unit) {
        val partial = File(directory, name + WakeelFolder.PARTIAL_SUFFIX)
        val target = File(directory, name)
        try {
            partial.outputStream().buffered().use { output ->
                body(output)
                output.flush()
            }
            if (target.exists()) target.delete()
            if (!partial.renameTo(target)) {
                throw java.io.IOException("the finished file could not be put in place")
            }
        } catch (exception: Throwable) {
            partial.delete()
            throw exception
        }
    }

    override fun delete(name: String) {
        File(directory, name).delete()
    }

    override fun clearPartials() {
        directory.listFiles().orEmpty()
            .filter { it.name.endsWith(WakeelFolder.PARTIAL_SUFFIX) }
            .forEach { it.delete() }
    }
}

/**
 * The folder as the application really has it: a tree the person chose once through the
 * system's folder picker, with the permission kept for good. Nothing here asks for a storage
 * permission, and nothing reaches outside the chosen folder.
 */
class DocumentFileWakeelFolder(
    private val context: Context,
    private val treeUri: Uri,
) : WakeelFolder {

    private val root: DocumentFile
        get() = DocumentFile.fromTreeUri(context, treeUri)
            ?: throw java.io.IOException("the chosen folder is no longer reachable")

    override fun list(): List<WakeelFileInfo> =
        root.listFiles()
            .filter { it.isFile && (it.name?.endsWith(WakeelFolder.PARTIAL_SUFFIX) == false) }
            .mapNotNull { file -> file.name?.let { WakeelFileInfo(it, file.length(), file.lastModified()) } }
            .sortedBy { it.name }

    override fun exists(name: String): Boolean = root.findFile(name)?.isFile == true

    override fun openRead(name: String): InputStream {
        val file = root.findFile(name) ?: throw java.io.FileNotFoundException(name)
        return context.contentResolver.openInputStream(file.uri)
            ?: throw java.io.IOException("the file could not be opened")
    }

    override fun write(name: String, body: (OutputStream) -> Unit) {
        val partialName = name + WakeelFolder.PARTIAL_SUFFIX
        root.findFile(partialName)?.delete()
        val partial = root.createFile("application/octet-stream", partialName)
            ?: throw java.io.IOException("the file could not be created in the chosen folder")
        try {
            val stream = context.contentResolver.openOutputStream(partial.uri)
                ?: throw java.io.IOException("the file could not be written")
            stream.buffered().use { output ->
                body(output)
                output.flush()
            }
            root.findFile(name)?.delete()
            if (!partial.renameTo(name)) {
                throw java.io.IOException("the finished file could not be put in place")
            }
        } catch (exception: Throwable) {
            partial.delete()
            throw exception
        }
    }

    override fun delete(name: String) {
        root.findFile(name)?.delete()
    }

    override fun clearPartials() {
        root.listFiles()
            .filter { it.name?.endsWith(WakeelFolder.PARTIAL_SUFFIX) == true }
            .forEach { it.delete() }
    }
}
