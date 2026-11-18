/** O IndexedDB existe neste navegador? (detecção para a tela de navegador sem suporte; o uso fica só na porta LocalStore) */
export const localStoreAvailable = () => typeof indexedDB !== 'undefined'
