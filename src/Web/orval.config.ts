import { defineConfig } from 'orval'

/** Cliente da API gerado do OpenAPI do Host (FND-09c CA 1). Atualize com: pnpm gen:api (depois do teste do OpenAPI). */
export default defineConfig({
  bemaqui: {
    input: { target: './openapi.json' },
    output: {
      target: 'src/api/generated/bemaqui.ts',
      schemas: 'src/api/generated/model',
      client: 'react-query',
      httpClient: 'fetch',
      clean: true,
      prettier: false,
      override: {
        mutator: { path: 'src/api/http.ts', name: 'apiFetch' },
        fetch: { includeHttpResponseReturnType: false },
      },
    },
  },
})
