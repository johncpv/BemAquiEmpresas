import { RouterProvider } from '@tanstack/react-router'
import { NavigationProvider } from '@/navigation/NavigationProvider'
import { router } from './router'

export function App({ appRouter = router }: { appRouter?: typeof router }) {
  return (
    <NavigationProvider>
      <RouterProvider router={appRouter} />
    </NavigationProvider>
  )
}
