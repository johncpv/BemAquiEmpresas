import { Link } from '@tanstack/react-router'
import { useGetItemStageTemplate } from '@/api/generated/bemaqui'
import { useHasProfile } from '@/navigation/navigation-context'
import { SERVICE_PROFILES } from '@/navigation/modules'
import { useCan } from '@/pages/use-can'

/**
 * Modelo de etapas do item de serviço na ficha do item (EXE-02 CA 10): só leitura, só para quem tem projects.view no perfil Serviços. O vínculo
 * se faz na aba Itens do modelo.
 */
export function ItemStageTemplate({ itemId, type }: { itemId: string; type: string }) {
  const services = useHasProfile(SERVICE_PROFILES)
  const canView = useCan('projects.view')
  const enabled = services && canView && type === 'Service'
  const query = useGetItemStageTemplate(itemId, { query: { enabled } })
  if (!enabled || !query.data) return null
  const t = query.data
  return (
    <span className="text-sm text-muted-foreground">
      Modelo de etapas:{' '}
      {t.stageTemplateId
        ? <Link to={'/app/projetos/modelos/$id' as never} params={{ id: t.stageTemplateId } as never} className="font-semibold text-primary hover:underline">{t.stageTemplateName}</Link>
        : 'nenhum (o orçamento usa o padrão)'}
    </span>
  )
}
