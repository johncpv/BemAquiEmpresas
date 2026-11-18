import { FloorState } from '@/components/shopfloor/kit'
import { Centered } from './Centered'

export function PdvNotFound() {
  return (
    <Centered>
      <FloorState title="Esta página não existe no caixa." action={<a href="/pdv/" className="inline-flex min-h-[56px] items-center rounded-md bg-primary px-5 text-[18px] font-semibold text-primary-foreground">Voltar ao caixa</a>} />
    </Centered>
  )
}
