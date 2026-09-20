package ps.wakeel.phone

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import ps.wakeel.phone.design.MBottomNav
import ps.wakeel.phone.design.MNavItem
import ps.wakeel.phone.design.MFab
import ps.wakeel.phone.design.MPhoneScreen
import ps.wakeel.phone.design.MStateView
import ps.wakeel.phone.design.MStatusBar
import ps.wakeel.phone.design.MTopBar
import ps.wakeel.phone.design.WakeelIcons
import ps.wakeel.phone.design.WakeelTheme

/**
 * The five places the bottom bar leads to. The order is the reading order, so the first one
 * ends up at the right edge of the bar, which is where the design puts the home screen.
 */
enum class WakeelDestination(val route: String, val labelRes: Int, val icon: Int) {
    Home("home", R.string.nav_home, WakeelIcons.Home),
    Correspondence("correspondence", R.string.nav_correspondence, WakeelIcons.Mail),
    Tasks("tasks", R.string.nav_tasks, WakeelIcons.ListChecks),
    Meetings("meetings", R.string.nav_meetings, WakeelIcons.Calendar),
    Search("search", R.string.nav_search, WakeelIcons.Search),
}

/**
 * The shell of the application: the theme, the frame of the phone kit, and a navigation host
 * that the screen packages fill in. Until they do, each destination shows the kit's own state
 * view rather than an invented placeholder.
 */
@Composable
fun WakeelShell(navController: NavHostController = rememberNavController()) {
    WakeelTheme {
        val entry by navController.currentBackStackEntryAsState()
        val route = entry?.destination?.route ?: WakeelDestination.Home.route
        val current = WakeelDestination.entries.firstOrNull { it.route == route } ?: WakeelDestination.Home
        val context = LocalContext.current

        MPhoneScreen(
            statusBar = { MStatusBar() },
            topBar = {
                MTopBar(
                    title = stringResource(current.labelRes),
                    onSearch = { navController.go(WakeelDestination.Search) },
                    searchDescription = stringResource(R.string.nav_search),
                    onMore = {},
                    moreDescription = stringResource(R.string.more),
                    navigationIcon = WakeelIcons.Menu,
                    navigationDescription = stringResource(R.string.menu),
                    onNavigation = {},
                )
            },
            bottomBar = {
                MBottomNav(
                    items = WakeelDestination.entries.map {
                        MNavItem(label = context.getString(it.labelRes), icon = it.icon)
                    },
                    selectedIndex = WakeelDestination.entries.indexOf(current),
                    onSelect = { index -> navController.go(WakeelDestination.entries[index]) },
                )
            },
            fab = {
                MFab(onClick = {}, contentDescription = stringResource(R.string.quick_entry))
            },
        ) {
            NavHost(
                navController = navController,
                startDestination = WakeelDestination.Home.route,
            ) {
                WakeelDestination.entries.forEach { destination ->
                    composable(destination.route) { Placeholder() }
                }
            }
        }
    }
}

/** What a destination shows before its own package fills it in. */
@Composable
private fun Placeholder() {
    MStateView(
        title = stringResource(R.string.shell_placeholder_title),
        description = stringResource(R.string.shell_placeholder_body),
        icon = WakeelIcons.Inbox,
    )
}

/** Moving between the five places never stacks them on top of each other. */
private fun NavHostController.go(destination: WakeelDestination) {
    navigate(destination.route) {
        popUpTo(graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = true
    }
}
