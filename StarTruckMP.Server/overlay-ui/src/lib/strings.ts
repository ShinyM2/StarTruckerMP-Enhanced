// ---------------------------------------------------------------------------
// The overlay's words.
//
// English is built in so the page is never blank; the game pushes the same keys in the
// player's own language (a "strings" message, from the one translation table on the C# side)
// and they replace these. Components read through t(), which is reactive, so a language
// change while the menu is open redraws it.
// ---------------------------------------------------------------------------
import { writable, get } from 'svelte/store';
import { onGameMessage } from '$lib/gameEvents';

const english: Record<string, string> = {
	'overlay.title': 'Multiplayer',
	'overlay.connected': 'connected',
	'overlay.disconnected': 'not connected',
	'overlay.close': 'Close',
	'overlay.tab.connect': 'Connection',
	'overlay.tab.host': 'Your server',
	'overlay.tab.chat': 'Chat',
	'overlay.tab.settings': 'Settings',
	'overlay.address': 'Server address',
	'overlay.address.placeholder': 'e.g. 203.0.113.10',
	'overlay.port': 'Port',
	'overlay.saveconnect': 'Save and connect',
	'overlay.you': 'You',
	'overlay.sector': 'Sector',
	'overlay.playerid': 'Player number',
	'overlay.orderhint':
		'Start order does not matter: if the server is not up yet, the client waits and connects by itself.',
	'overlay.host.running': 'The server is running on this machine.',
	'overlay.host.stop': 'Stop the server',
	'overlay.host.start': 'Start the server',
	'overlay.host.hint':
		'For friends to join, forward port 7777 — TCP and UDP — on your router to this computer and give them your public IP. Keep the game open while the server runs.',
	'overlay.host.missing':
		'There is no StarTruckMP.Server.exe next to the plugin, so nothing to start. The server can also run on its own — it ships with the release.',
	'overlay.chat.empty': 'No messages yet.',
	'overlay.chat.all': 'everyone',
	'overlay.chat.placeholder': 'Message…',
	'overlay.chat.send': 'Send',
	'overlay.chat.sectoronly': 'Only my sector',
	'overlay.set.nameplates': 'Nameplates above trucks',
	'overlay.set.nameplates.hint': 'Applies to players who appear after the change.',
	'overlay.set.collisions': "Collide with other players' trucks",
	'overlay.set.collisions.hint':
		'Off by default: with any latency the other truck collides where it visually is not.',
	'overlay.set.ssl': 'Do not verify the server certificate',
	'overlay.set.ssl.hint': 'Needed for servers with a self-signed certificate — that is, nearly all of them.',
	'overlay.footer': 'F2 — open and close this menu, Esc — close',
	'overlay.players.onserver': 'on server',
	'overlay.players.insector': '{0} in your sector',
	'overlay.players.unknown': 'unknown'
};

export const strings = writable<Record<string, string>>({ ...english });

onGameMessage<Record<string, string>>('strings', (message) => {
	if (!message.payload) return;
	strings.set({ ...english, ...message.payload });
});

/** A word in the player's language, with {0}-style arguments filled in. Reactive through the store. */
export function translate(table: Record<string, string>, key: string, ...args: unknown[]): string {
	let text = table[key] ?? english[key] ?? key;
	args.forEach((arg, i) => {
		text = text.replace(`{${i}}`, String(arg));
	});
	return text;
}

/** Non-reactive lookup for code outside a component template. */
export function t(key: string, ...args: unknown[]): string {
	return translate(get(strings), key, ...args);
}
