package ps.wakeel.phone

import android.app.Application

/**
 * The application object. It deliberately does nothing at start up beyond existing: the
 * database key lives behind the Android Keystore and is only unwrapped once the person has
 * unlocked the application, so nothing is opened here.
 */
class WakeelApplication : Application()
