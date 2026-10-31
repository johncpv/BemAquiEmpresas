import { useQuery } from '@tanstack/react-query'
import { ImageOff, Tag } from 'lucide-react'
import { getDownloadAssetFileUrl } from '@/api/generated/bemaqui'
import { apiBlobUrl } from '@/api/http'
import { cn } from '@/lib/utils'

/**
 * Foto do bem (CA 8): só a foto limpa (depois do antivírus), já sem EXIF/GPS, lida pela rota do bem e mostrada como endereço local
 * (blob:). Sem foto, o ícone de etiqueta.
 */
export function AssetPhoto({ assetId, photoId, name, size = 'thumb' }: { assetId: string; photoId?: string | null; name: string; size?: 'thumb' | 'large' }) {
  const photo = useQuery({
    queryKey: ['asset-photo', assetId, photoId],
    queryFn: () => apiBlobUrl(getDownloadAssetFileUrl(assetId, photoId!)),
    enabled: !!photoId,
    // A mesma foto (id) nunca muda: fica em cache na sessão, sem baixar de novo a cada página da lista.
    staleTime: Infinity,
  })
  const box = size === 'thumb' ? 'size-10' : 'aspect-[4/3] w-full max-w-sm'
  if (!photoId || photo.isError) {
    const Icon = photo.isError ? ImageOff : Tag
    return (
      <span className={cn('grid shrink-0 place-items-center rounded-md border border-border bg-muted text-muted-foreground', box)}>
        <Icon aria-hidden="true" className={size === 'thumb' ? 'size-4' : 'size-8'} />
        {size === 'large' ? <span className="sr-only">{photo.isError ? 'Não foi possível carregar a foto.' : 'Sem foto.'}</span> : null}
      </span>
    )
  }

  if (!photo.data) return <span aria-hidden="true" className={cn('shrink-0 animate-pulse rounded-md bg-muted', box)} />
  return <img src={photo.data} alt={size === 'thumb' ? '' : `Foto de ${name}`} className={cn('shrink-0 rounded-md border border-border object-cover', box)} />
}
