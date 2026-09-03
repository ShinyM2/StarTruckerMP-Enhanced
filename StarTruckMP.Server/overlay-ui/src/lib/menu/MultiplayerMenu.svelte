<script lang="ts">
	import { onDestroy } from 'svelte';
	import { onGameMessage, sendToGame } from '$lib/gameEvents';
	import { strings, translate } from '$lib/strings';

	// The player's words, redrawn whenever the game sends a new set.
	let tr = $derived.by(() => (key: string, ...args: unknown[]) => translate($strings, key, ...args));

	interface Settings {
		serverAddress: string;
		serverPort: string;
		ignoreSslValidation: boolean;
		showNameplates: boolean;
		remoteCollisions: boolean;
	}

	interface Status {
		connected: boolean;
		netId: number;
		sector: string;
		name: string;
		hosting: boolean;
		serverAvailable: boolean;
	}

	interface ChatLine {
		netId: number;
		name: string;
		message: string;
		sectorOnly: boolean;
		mine: boolean;
	}

	type Tab = 'connect' | 'host' | 'chat' | 'settings';

	let open = $state(false);
	let tab = $state<Tab>('connect');

	let settings = $state<Settings | null>(null);
	let status = $state<Status | null>(null);
	let notice = $state('');
	let noticeTimer: ReturnType<typeof setTimeout> | undefined;

	let chat = $state<ChatLine[]>([]);
	let draft = $state('');
	let sectorOnly = $state(true);
	let chatBody = $state<HTMLDivElement | null>(null);

	// Draft copies so typing does not fight the values pushed from the game.
	let addressDraft = $state('');
	let portDraft = $state('');

	const offs = [
		onGameMessage<Settings>('settings', (m) => {
			settings = m.payload ?? null;
			if (settings) {
				addressDraft = settings.serverAddress;
				portDraft = settings.serverPort;
			}
		}),
		onGameMessage<Status>('status', (m) => (status = m.payload ?? null)),
		onGameMessage<ChatLine>('chat', (m) => {
			if (m.payload) appendChat(m.payload);
		}),
		onGameMessage<ChatLine[]>('chatHistory', (m) => {
			chat = m.payload ?? [];
			scrollChat();
		}),
		onGameMessage<{ text: string }>('notice', (m) => showNotice(m.payload?.text ?? '')),
		onGameMessage<{ visible: boolean }>('menu', (m) => {
			open = m.payload?.visible ?? false;
			if (open) sendToGame('menuOpened', {});
		})
	];

	onDestroy(() => offs.forEach((off) => off()));

	function appendChat(line: ChatLine) {
		chat = [...chat.slice(-99), line];
		scrollChat();
	}

	function scrollChat() {
		queueMicrotask(() => {
			if (chatBody) chatBody.scrollTop = chatBody.scrollHeight;
		});
	}

	function showNotice(text: string) {
		notice = text;
		clearTimeout(noticeTimer);
		noticeTimer = setTimeout(() => (notice = ''), 6000);
	}

	function send() {
		const message = draft.trim();
		if (!message) return;
		sendToGame('chatSend', { message, sectorOnly });
		draft = '';
	}

	function saveConnection() {
		sendToGame('settingsSave', { serverAddress: addressDraft, serverPort: portDraft });
	}

	function toggle(key: keyof Settings, value: boolean) {
		if (!settings) return;
		settings = { ...settings, [key]: value };
		sendToGame('settingsSave', { [key]: value });
	}

	function prettySector(sector: string): string {
		if (!sector || sector === 'none') return '—';
		return sector
			.replace(/^Sector[_-]?\d*[_-]?/i, '')
			.replace(/[_-]+/g, ' ')
			.replace(/([a-z\d])([A-Z])/g, '$1 $2')
			.trim();
	}
</script>

{#if open}
	<div class="scrim">
		<section class="menu">
			<header>
				<h1>{tr('overlay.title')}</h1>
				<span class="state" class:on={status?.connected}>
					{status?.connected ? tr('overlay.connected') : tr('overlay.disconnected')}
				</span>
				<button class="close" onclick={() => sendToGame('menuClose', {})} aria-label={tr('overlay.close')}>✕</button>
			</header>

			<nav>
				<button class:active={tab === 'connect'} onclick={() => (tab = 'connect')}>{tr('overlay.tab.connect')}</button>
				<button class:active={tab === 'host'} onclick={() => (tab = 'host')}>{tr('overlay.tab.host')}</button>
				<button class:active={tab === 'chat'} onclick={() => (tab = 'chat')}>{tr('overlay.tab.chat')}</button>
				<button class:active={tab === 'settings'} onclick={() => (tab = 'settings')}>{tr('overlay.tab.settings')}</button>
			</nav>

			<div class="body">
				{#if tab === 'connect'}
					<div class="rows">
						<label>
							<span>{tr('overlay.address')}</span>
							<input bind:value={addressDraft} placeholder={tr('overlay.address.placeholder')} spellcheck="false" />
						</label>
						<label>
							<span>{tr('overlay.port')}</span>
							<input bind:value={portDraft} placeholder="7777" spellcheck="false" />
						</label>
						<button class="primary" onclick={saveConnection}>{tr('overlay.saveconnect')}</button>
					</div>

					<dl class="facts">
						<div><dt>{tr('overlay.you')}</dt><dd>{status?.name || '—'}</dd></div>
						<div><dt>{tr('overlay.sector')}</dt><dd>{prettySector(status?.sector ?? '')}</dd></div>
						<div><dt>{tr('overlay.playerid')}</dt><dd>{status && status.netId >= 0 ? status.netId : '—'}</dd></div>
					</dl>

					<p class="hint">{tr('overlay.orderhint')}</p>
				{:else if tab === 'host'}
					{#if status?.serverAvailable}
						<div class="rows">
							{#if status?.hosting}
								<p class="running">{tr('overlay.host.running')}</p>
								<button class="danger" onclick={() => sendToGame('hostStop', {})}>{tr('overlay.host.stop')}</button>
							{:else}
								<button class="primary" onclick={() => sendToGame('hostStart', {})}>{tr('overlay.host.start')}</button>
							{/if}
						</div>
						<p class="hint">{tr('overlay.host.hint')}</p>
					{:else}
						<p class="hint">{tr('overlay.host.missing')}</p>
					{/if}
				{:else if tab === 'chat'}
					<div class="chat-body" bind:this={chatBody}>
						{#if chat.length === 0}
							<p class="empty">{tr('overlay.chat.empty')}</p>
						{/if}
						{#each chat as line, i (i)}
							<p class="line" class:mine={line.mine}>
								<span class="who">{line.name}</span>
								{#if !line.sectorOnly}<span class="scope">{tr('overlay.chat.all')}</span>{/if}
								<span class="text">{line.message}</span>
							</p>
						{/each}
					</div>

					<form class="composer" onsubmit={(e) => { e.preventDefault(); send(); }}>
						<input bind:value={draft} maxlength="300" placeholder={tr('overlay.chat.placeholder')} />
						<button type="submit" class="primary">{tr('overlay.chat.send')}</button>
					</form>
					<label class="check">
						<input type="checkbox" bind:checked={sectorOnly} />
						<span>{tr('overlay.chat.sectoronly')}</span>
					</label>
				{:else if settings}
					<div class="rows">
						<label class="check">
							<input
								type="checkbox"
								checked={settings.showNameplates}
								onchange={(e) => toggle('showNameplates', e.currentTarget.checked)}
							/>
							<span>{tr('overlay.set.nameplates')}<small>{tr('overlay.set.nameplates.hint')}</small></span>
						</label>

						<label class="check">
							<input
								type="checkbox"
								checked={settings.remoteCollisions}
								onchange={(e) => toggle('remoteCollisions', e.currentTarget.checked)}
							/>
							<span>{tr('overlay.set.collisions')}<small>{tr('overlay.set.collisions.hint')}</small></span>
						</label>

						<label class="check">
							<input
								type="checkbox"
								checked={settings.ignoreSslValidation}
								onchange={(e) => toggle('ignoreSslValidation', e.currentTarget.checked)}
							/>
							<span>{tr('overlay.set.ssl')}<small>{tr('overlay.set.ssl.hint')}</small></span>
						</label>
					</div>
				{/if}
			</div>

			{#if notice}
				<div class="notice">{notice}</div>
			{/if}

			<footer>{tr('overlay.footer')}</footer>
		</section>
	</div>
{/if}

<style>
	.scrim {
		position: absolute;
		inset: 0;
		display: grid;
		place-items: center;
		background: rgba(4, 6, 8, 0.55);
	}

	.menu {
		width: min(560px, 86vw);
		max-height: 82vh;
		display: flex;
		flex-direction: column;
		background: var(--st-panel-solid);
		border: 1px solid var(--st-line);
		box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.6), 0 26px 70px rgba(0, 0, 0, 0.6);
		font-size: 14px;
	}

	header {
		display: flex;
		align-items: center;
		gap: 12px;
		padding: 13px 16px 11px;
		border-bottom: 1px solid var(--st-line);
	}

	h1 {
		margin: 0;
		font-size: 15px;
		font-weight: 600;
		letter-spacing: 0.18em;
		text-transform: uppercase;
		color: var(--st-amber);
	}

	.state {
		font-size: 10px;
		text-transform: uppercase;
		letter-spacing: 0.14em;
		color: var(--st-muted);
	}

	.state.on { color: var(--st-ok); }

	.close {
		margin-left: auto;
		background: none;
		border: none;
		color: var(--st-muted);
		font-size: 15px;
		cursor: pointer;
		padding: 2px 6px;
	}

	.close:hover { color: var(--st-amber); }

	nav {
		display: flex;
		border-bottom: 1px solid var(--st-line-soft);
	}

	nav button {
		flex: 1;
		background: none;
		border: none;
		border-bottom: 2px solid transparent;
		color: var(--st-muted);
		padding: 9px 4px;
		font-size: 11px;
		letter-spacing: 0.12em;
		text-transform: uppercase;
		font-family: inherit;
		cursor: pointer;
	}

	nav button:hover { color: var(--st-text); }
	nav button.active { color: var(--st-amber); border-bottom-color: var(--st-amber); }

	.body {
		padding: 15px 16px 6px;
		overflow-y: auto;
		display: flex;
		flex-direction: column;
		gap: 13px;
	}

	.rows { display: flex; flex-direction: column; gap: 11px; }

	label { display: flex; flex-direction: column; gap: 5px; }

	label > span {
		font-size: 10px;
		letter-spacing: 0.16em;
		text-transform: uppercase;
		color: var(--st-amber-soft);
	}

	input:not([type='checkbox']) {
		background: var(--st-field);
		border: 1px solid var(--st-line-soft);
		color: var(--st-text);
		padding: 8px 10px;
		font-size: 14px;
		font-family: inherit;
	}

	input:focus-visible {
		outline: none;
		border-color: var(--st-amber);
	}

	button.primary, button.danger {
		border: 1px solid var(--st-amber);
		background: none;
		color: var(--st-amber);
		padding: 9px 16px;
		font-size: 11px;
		font-weight: 600;
		letter-spacing: 0.14em;
		text-transform: uppercase;
		font-family: inherit;
		cursor: pointer;
	}

	button.primary:hover { background: var(--st-amber); color: #14120a; }

	button.danger { border-color: var(--st-danger); color: var(--st-danger); }
	button.danger:hover { background: var(--st-danger); color: #1a0d07; }

	.facts {
		display: flex;
		gap: 22px;
		margin: 0;
		padding: 11px 0 0;
		border-top: 1px solid var(--st-line-soft);
	}

	.facts div { display: flex; flex-direction: column; gap: 3px; }

	.facts dt {
		font-size: 10px;
		text-transform: uppercase;
		letter-spacing: 0.14em;
		color: var(--st-amber-soft);
	}

	.facts dd { margin: 0; font-size: 14px; }

	.hint { margin: 0; font-size: 12px; line-height: 1.55; color: var(--st-muted); }
	.running { margin: 0; color: var(--st-ok); letter-spacing: 0.04em; }

	code {
		background: var(--st-field);
		border: 1px solid var(--st-line-soft);
		padding: 1px 5px;
		font-size: 12px;
		color: var(--st-amber);
	}

	.chat-body {
		height: 240px;
		overflow-y: auto;
		display: flex;
		flex-direction: column;
		gap: 5px;
		padding: 9px 11px;
		background: rgba(0, 0, 0, 0.35);
		border: 1px solid var(--st-line-soft);
	}

	.line { margin: 0; line-height: 1.45; word-break: break-word; }
	.who { color: var(--st-amber); font-weight: 600; margin-right: 6px; }
	.line.mine .who { color: var(--st-ok); }

	.scope {
		font-size: 10px;
		text-transform: uppercase;
		letter-spacing: 0.12em;
		color: var(--st-muted);
		margin-right: 6px;
	}

	.empty { margin: auto; color: var(--st-muted); font-size: 12px; }

	.composer { display: flex; gap: 8px; }
	.composer input { flex: 1; }

	.check { flex-direction: row; align-items: flex-start; gap: 10px; }

	.check span {
		color: var(--st-text);
		font-size: 13px;
		letter-spacing: 0;
		text-transform: none;
		display: flex;
		flex-direction: column;
		gap: 3px;
	}

	.check small { color: var(--st-muted); font-size: 11px; line-height: 1.5; }
	.check input { margin-top: 3px; accent-color: var(--st-amber); }

	.notice {
		margin: 4px 16px 0;
		padding: 8px 11px;
		background: rgba(239, 200, 6, 0.1);
		border-left: 2px solid var(--st-amber);
		font-size: 12px;
	}

	footer {
		padding: 11px 16px 12px;
		font-size: 10px;
		letter-spacing: 0.1em;
		text-transform: uppercase;
		color: var(--st-muted);
	}
</style>
