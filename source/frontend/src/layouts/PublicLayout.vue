<template>
  <div class="public-shell">
    <RouterView />
    <footer class="public-footer faint small">Brewline · Gọi món tại bàn</footer>
  </div>
</template>

<style scoped>
.public-shell { min-height: 100vh; display: flex; flex-direction: column; }
.public-footer { text-align: center; padding: 24px 0 32px; }
</style>
