import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { type ReactNode, useEffect, useState } from 'react'
import { useGetAssetByTag, useGetAssetCard } from '@/api/generated/bemaqui'
import type { AssetCardView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorHeader, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { StatusChip } from '@/components/ui/status-chip'
import { useHelp } from '@/help/help-context'
import { formatDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { AssetPhoto } from './AssetPhoto'
import { STATUS_LABELS, STATUS_TONES, TYPE_LABELS } from './assets'
import { cardRouteFor } from './labels'
import { ReadTagEntry, ReadTagSheet } from './TagReader'

const PIN_SCOPE = 'pin_session_scope'
const PIN_MESSAGE = 'A leitura de etiqueta não abre na sessão por PIN do tablet. Entre com o seu login no celular ou no tablet.'

const shortcut = 'flex min-h-[56px] w-full items-center justify-center rounded-md px-5 text-center text-[18px] font-semibold'

/**
 * `/b/{etiqueta}` (PAT-01b CA 2, 5, 7 e 8): a URL do QR. Sem sessão, o login volta para cá (só caminho relativo); com `assets.view`, abre a
 * ficha resumida no celular e no tablet (`/fab/bens/{id}`) ou a ficha completa no computador. "Etiqueta não encontrada." é a mesma para
 * etiqueta inexistente, excluída ou de outro tenant; sem a permissão, a tela padrão de acesso.
 */
export function TagPage() {
  const { tag } = useParams({ strict: false }) as { tag: string }
  const help = useHelp()
  const navigate = useNavigate()
  const query = useGetAssetByTag(tag, { query: { retry: false } })
  const [reading, setReading] = useState(false)
  const id = query.data?.id

  useEffect(() => { document.title = 'Etiqueta do bem · BemAqui Empresas' }, [])
  useEffect(() => {
    if (id) void navigate({ to: cardRouteFor(id), replace: true })
  }, [id, navigate])

  const header = <FloorHeader onHelp={() => help.setOpen(true)} title={tag} subtitle="Etiqueta do patrimônio" />
  const again = <FloorButton onClick={() => setReading(true)}>Ler ou digitar outra etiqueta</FloorButton>
  let body
  if (query.isPending || query.isSuccess) body = <FloorSkeleton rows={2} />
  else {
    const error = query.error instanceof ApiError ? query.error : null
    body = error?.status === 404
      ? <FloorState title="Etiqueta não encontrada." message="Confira o número impresso na etiqueta e digite de novo. Se a etiqueta estiver danificada, peça a reimpressão a quem cuida do patrimônio." action={again} />
      : error?.code === PIN_SCOPE
        ? <FloorState title="Leitura indisponível neste tablet." message={PIN_MESSAGE} />
        : error?.status === 403
          ? <FloorState title="Sem acesso ao patrimônio." message="Seu usuário não tem permissão para ver os bens. Peça ao administrador para incluir você num grupo com acesso." action={<Link to="/app" className="inline-flex min-h-[56px] items-center text-[18px] font-semibold text-primary">Voltar ao início</Link>} />
          : <FloorState tone="error" title="Não foi possível abrir a etiqueta." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />
  }

  return (
    <main id="conteudo" className="min-h-dvh bg-background pb-8 text-[18px] text-foreground">
      {header}
      <div className="mx-auto max-w-3xl space-y-4 p-4">{body}</div>
      <ReadTagSheet open={reading} onOpenChange={setReading} />
    </main>
  )
}

/**
 * Ficha resumida do bem no celular e no tablet (`/fab/bens/{id}`, PAT-01b CA 5 e 6): foto, etiqueta, nome, tipo, local, responsável, centro,
 * situação e a manutenção; os atalhos "Abrir OS de manutenção corretiva" (`assets.create`, PAT-03a) e "Registrar movimentação"
 * (`assets.edit`, PAT-02) e "Ver ficha completa". Alvos de 56 px; desenhada para 400 px.
 */
export function FabAssetPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const help = useHelp()
  const query = useGetAssetCard(id, { query: { retry: false } })
  useEffect(() => { document.title = 'Bem · BemAqui Empresas' }, [])

  const asset = query.data
  const header = (
    <FloorHeader onHelp={() => help.setOpen(true)} title={asset ? asset.tag : 'Bem do patrimônio'} subtitle="Patrimônio">
      <span className="max-sm:hidden"><ReadTagEntry tone="button" /></span>
    </FloorHeader>
  )
  if (query.isPending) return <>{header}<div className="mx-auto max-w-3xl p-4"><FloorSkeleton /></div></>
  if (query.isError || !asset) {
    const error = query.error instanceof ApiError ? query.error : null
    return (
      <>{header}<div className="mx-auto max-w-3xl p-4">
        {error?.status === 404 ? <FloorState title="Bem não encontrado." message="Ele pode ter sido excluído. Leia outra etiqueta." action={<ReadTagEntry tone="button" />} />
          : error?.code === PIN_SCOPE ? <FloorState title="Leitura indisponível neste tablet." message={PIN_MESSAGE} />
            : <FloorState tone="error" title="Não foi possível carregar o bem." message="Verifique a conexão e tente de novo." action={<FloorButton onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />}
      </div></>
    )
  }

  return (
    <>
      {header}
      <div className="mx-auto max-w-3xl space-y-4 p-4">
        <AssetCard asset={asset} />
        <section aria-label="Próximos passos" className="space-y-[8px]">
          <Can perm="assets.create">
            <Link to="/app/patrimonio/bens/$id" params={{ id: asset.id }} search={{ aba: 'manutencao' } as never} data-floor-target="" className={shortcut + ' bg-primary text-primary-foreground'}>Abrir OS de manutenção corretiva</Link>
          </Can>
          <Can perm="assets.edit">
            <Link to="/app/patrimonio/bens/$id" params={{ id: asset.id }} search={{ aba: 'movimentacoes' } as never} data-floor-target="" className={shortcut + ' border-2 border-input bg-card'}>Registrar movimentação</Link>
          </Can>
          <Link to="/app/patrimonio/bens/$id" params={{ id: asset.id }} data-floor-target="" className={shortcut + ' border-2 border-input bg-card'}>Ver ficha completa</Link>
          <div className="sm:hidden"><ReadTagEntry tone="button" /></div>
        </section>
      </div>
    </>
  )
}

function AssetCard({ asset }: { asset: AssetCardView }) {
  const rows: [string, ReactNode][] = [
    ['Tipo', TYPE_LABELS[asset.type]],
    ['Local', asset.siteName ?? 'Sem local'],
    ['Responsável', asset.responsibleName ? <span key="responsavel" className="inline-flex flex-wrap items-center gap-2">{asset.responsibleName}{asset.responsibleTerminated ? <StatusChip tone="warn">Desligado</StatusChip> : null}</span> : 'Sem responsável'],
    ['Centro de trabalho', asset.workCenterName ?? 'Sem centro'],
    ['Próxima manutenção', asset.nextMaintenanceDate ? formatDate(asset.nextMaintenanceDate) : 'Sem plano de manutenção'],
    ['Última manutenção', asset.lastMaintenanceDate ? formatDate(asset.lastMaintenanceDate) : 'Nenhuma registrada'],
  ]
  return (
    <article aria-labelledby="bem-nome" className="space-y-4 rounded-md border-2 border-border bg-card p-4">
      <AssetPhoto assetId={asset.id} photoId={asset.photoId} name={asset.name} size="large" />
      <div className="space-y-2">
        <p className="font-display text-[26px] font-bold font-wider tabular-nums">{asset.tag}</p>
        <h1 id="bem-nome" className="text-[22px] font-bold leading-tight">{asset.name}</h1>
        <StatusChip tone={STATUS_TONES[asset.status]}>{STATUS_LABELS[asset.status]}</StatusChip>
      </div>
      <dl className="divide-y divide-border">
        {rows.map(([label, value]) => (
          <div key={label} className="flex flex-wrap justify-between gap-x-4 gap-y-1 py-3">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="font-semibold tabular-nums">{value}</dd>
          </div>
        ))}
      </dl>
    </article>
  )
}
