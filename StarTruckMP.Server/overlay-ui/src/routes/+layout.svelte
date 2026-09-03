<script lang="ts">
	import favicon from '$lib/assets/favicon.svg';
	import { dev } from '\$app/environment';
	import bkgimg from '$lib/assets/game-screenshot.webp';

	let { children } = $props();
	
	let bg = dev ? `center / contain no-repeat url("${bkgimg}")` : `transparent`;
	console.log(bg);
	// show image while developing
	window.CSS.registerProperty({
		name: '--background',
		syntax: '*',
		inherits: false,
		initialValue: bg
	})
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{@render children()}

<style>
	/*
	 * Star Trucker's own interface: amber on dark translucent panels, condensed uppercase
	 * labels, hairline borders. Everything the mod draws uses these tokens so it reads as part
	 * of the game rather than a web page floating on top of it.
	 */
	:global(:root) {
		--st-amber: #efc806;
		--st-amber-soft: #d8b40a;
		--st-text: #e9e7de;
		--st-muted: #9b978a;
		--st-ok: #86c96e;
		--st-danger: #d8703f;

		--st-panel: rgba(10, 12, 15, 0.74);
		--st-panel-solid: rgba(8, 10, 13, 0.95);
		--st-line: rgba(239, 200, 6, 0.38);
		--st-line-soft: rgba(239, 200, 6, 0.14);
		--st-field: rgba(255, 255, 255, 0.05);

		/* Bahnschrift ships with Windows and is the closest match to the game's condensed UI face. */
		--st-font: "Bahnschrift", "Segoe UI Variable Display", "Segoe UI Semibold", "Arial Narrow", system-ui, sans-serif;
	}

	:global(html),
	:global(body)
	{
		/* full width and height with transparent background */
		width: 100%;
		height: 100%;
		background: var(--background);
		margin: 0;
		padding: 0;
		overflow: hidden;
	}

	:global(*) {
		font-family: var(--st-font);
		color: var(--st-text);
		box-sizing: border-box;
	}

	/* Uppercase amber caption, the game's label idiom. */
	:global(.st-label) {
		font-size: 10px;
		letter-spacing: 0.16em;
		text-transform: uppercase;
		color: var(--st-amber-soft);
	}
</style>
