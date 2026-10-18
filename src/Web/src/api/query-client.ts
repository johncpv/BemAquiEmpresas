import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './http'

/** Estado do servidor só no TanStack Query (02-arquitetura §4). 4xx não é repetido; o resto tenta mais uma vez. */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 1,
      refetchOnWindowFocus: false,
    },
  },
})
