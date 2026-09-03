<script lang="ts">
	import { onDestroy } from 'svelte';
	import { onGameMessage } from '$lib/gameEvents';
	import { strings, translate } from '$lib/strings';

	let tr = $derived.by(() => (key: string, ...args: unknown[]) => translate($strings, key, ...args));

	interface RosterEntry {
		netId: number;
		name: string;
		sector: string;
		sameSector: boolean;
		/** Milliseconds to the server, as the server measures it; -1 before it has said. */
		ping: number;
	}

	interface Roster {
		total: number;
		self: RosterEntry;
		players: RosterEntry[];
	}

	let roster = $state<Roster | null>(null);

	const offs = [
		onGameMessage<Roster>('players', (message) => {
			roster = message.payload ?? null;
		}),
		onGameMessage<{ inWorld: boolean }>('hud', (message) => {
			inWorld = message.payload?.inWorld ?? false;
		})
	];

	onDestroy(() => offs.forEach((off) => off()));

	/** "Sector_02_AtlasPrime" reads better as "Atlas Prime". */
	function prettySector(sector: string): string {
		if (!sector || sector === 'none') return tr('overlay.players.unknown');

		return sector
			.replace(/^Sector[_-]?\d*[_-]?/i, '')
			.replace(/[_-]+/g, ' ')
			.replace(/([a-z\d])([A-Z])/g, '$1 $2')
			.trim();
	}

	// Everyone we can actually see right now, us included.
	let here = $derived(
		roster ? roster.players.filter((p) => p.sameSector).length + 1 : 0
	);

	let elsewhere = $derived(roster ? roster.players.filter((p) => !p.sameSector) : []);
	let nearby = $derived(roster ? roster.players.filter((p) => p.sameSector) : []);

	/** Blank rather than a placeholder: an empty cell reads as "not yet", a "-1" as a fault. */
	function prettyPing(ping: number): string {
		return ping >= 0 ? `${ping} ms` : '';
	}

	// The game tells us outright whether the player is in the cockpit or sitting in a menu;
	// the sector alone is not enough, since it keeps its last value after leaving a save.
	let inWorld = $state(false);
</script>

{#if roster && inWorld}
	<aside class="panel">
		<header>
			<span class="count">{roster.total}</span>
			<span class="label">{tr('overlay.players.onserver')}</span>
			{#if roster.total > 1}
				<span class="here">{tr('overlay.players.insector', here)}</span>
			{/if}
		</header>

		<ul>
			<li class="self">
				<span class="dot here-dot"></span>
				<span class="name">{roster.self.name}</span>
				<span class="sector">{prettySector(roster.self.sector)}</span>
				<span class="ping">{prettyPing(roster.self.ping)}</span>
			</li>

			{#each nearby as player (player.netId)}
				<li>
					<span class="dot here-dot"></span>
					<span class="name">{player.name}</span>
					<span class="sector">{prettySector(player.sector)}</span>
					<span class="ping">{prettyPing(player.ping)}</span>
				</li>
			{/each}

			{#each elsewhere as player (player.netId)}
				<li class="away">
					<span class="dot"></span>
					<span class="name">{player.name}</span>
					<span class="sector">{prettySector(player.sector)}</span>
					<span class="ping">{prettyPing(player.ping)}</span>
				</li>
			{/each}
		</ul>
	</aside>
{/if}

<style>
	.panel {
		/* Flush to the right edge, vertically centred: the one part of the cockpit view
		   that stays clear of the mission list, the controls card and the dashboard. */
		position: absolute;
		top: 50%;
		right: 0;
		transform: translateY(-50%);
		min-width: 220px;
		max-width: 320px;
		padding: 9px 12px 8px;
		background: var(--st-panel);
		border: 1px solid var(--st-line);
		border-right: none;
		font-size: 13px;
		line-height: 1.4;
		pointer-events: none;
	}

	header {
		display: flex;
		align-items: baseline;
		gap: 7px;
		padding-bottom: 6px;
		margin-bottom: 7px;
		border-bottom: 1px solid var(--st-line-soft);
		white-space: nowrap;
	}

	.count {
		color: var(--st-amber);
		font-size: 18px;
		font-weight: 600;
		font-variant-numeric: tabular-nums;
		line-height: 1;
	}

	.label {
		color: var(--st-amber-soft);
		text-transform: uppercase;
		letter-spacing: 0.16em;
		font-size: 10px;
	}

	.here {
		margin-left: auto;
		color: var(--st-ok);
		font-size: 11px;
		letter-spacing: 0.04em;
	}

	ul {
		list-style: none;
		margin: 0;
		padding: 0;
		display: flex;
		flex-direction: column;
		gap: 3px;
	}

	li {
		display: grid;
		grid-template-columns: 7px 1fr auto auto;
		align-items: center;
		gap: 8px;
	}

	.dot {
		width: 5px;
		height: 5px;
		background: var(--st-muted);
	}

	.here-dot { background: var(--st-ok); }

	.name {
		color: var(--st-text);
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.self .name {
		color: var(--st-amber);
		font-weight: 600;
	}

	.sector {
		color: var(--st-muted);
		font-size: 11px;
		white-space: nowrap;
		text-transform: uppercase;
		letter-spacing: 0.06em;
	}

	.ping {
		/* Right-aligned and tabular so the column does not dance as the numbers change. */
		min-width: 42px;
		color: var(--st-muted);
		font-size: 11px;
		font-variant-numeric: tabular-nums;
		text-align: right;
		white-space: nowrap;
	}

	.away .name,
	.away .sector,
	.away .ping { opacity: 0.5; }
</style>
