<script lang="ts">
    import { sendToGame } from "$lib/gameEvents";
    import PlayerList from "$lib/players/PlayerList.svelte";
    import MultiplayerMenu from "$lib/menu/MultiplayerMenu.svelte";

    sendToGame("overlayLoaded", { timestamp: Date.now() });
</script>

<div class="container">
    <PlayerList />
    <MultiplayerMenu />
</div>

<style>
    .container {
        position: absolute;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
    }
</style>
