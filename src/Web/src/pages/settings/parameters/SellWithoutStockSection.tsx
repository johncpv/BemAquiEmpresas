import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetTenantParametersQueryKey, saveSellWithoutStock } from '@/api/generated/bemaqui'
import type { TenantParametersDto } from '@/api/generated/model'
import { ToggleGroup } from '@/components/form/choice'
import { FormSection } from '@/components/form/form'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'

const OPTIONS = [
  { value: 'no', label: 'Não vender sem saldo' },
  { value: 'yes', label: 'Vender sem saldo' },
]

const DICA = 'Desligado, o caixa não deixa vender item sem saldo na loja; sem internet, ele confere pelo último saldo recebido. Ligado, a venda sai e a falta aparece em Estoque, Divergências, para ajustar. A venda concluída nunca é recusada depois.'

/** Loja: vender sem saldo no caixa (PDV-05, PP-02 de 18/11/2026), padrão desligado. Só aparece para empresa de comércio. */
export function SellWithoutStockSection({ params, canEdit }: { params: TenantParametersDto; canEdit: boolean }) {
  const current = params.sellWithoutStock ? 'yes' : 'no'
  const [value, setValue] = useState(current)
  const client = useQueryClient()
  const enabled = value === 'yes'
  return (
    <FormSection title="Loja: venda sem saldo" description="O que o caixa faz quando o item não tem saldo de estoque na loja." grid="none">
      <div className="space-y-3">
        <div className="space-y-1">
          <p className="inline-flex items-center text-sm font-semibold">Item sem saldo no caixa<Dica nome="Venda sem saldo" texto={DICA} /></p>
          <ToggleGroup label="Item sem saldo no caixa" options={OPTIONS} value={value} onChange={setValue} disabled={!canEdit} />
        </div>
        {canEdit ? (
          <div className="flex justify-end">
            <ConfirmDialog verb="Salvar venda sem saldo" done="Venda sem saldo salva" title="Salvar venda sem saldo?"
              description={enabled
                ? 'O caixa passa a vender item sem saldo. Cada falta aparece em Estoque, Divergências, para ajustar o estoque e aplicar a baixa.'
                : 'O caixa passa a recusar item sem saldo na loja, pelo último saldo recebido.'}
              onConfirm={async () => {
                const saved = await saveSellWithoutStock({ enabled })
                client.setQueryData(getGetTenantParametersQueryKey(), saved)
                void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/TenantSetting/') })
              }}
              trigger={<button type="button" disabled={value === current} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar venda sem saldo</button>} />
          </div>
        ) : null}
        {params.sellWithoutStockSettingId ? (
          <details className="rounded-md border border-border bg-card px-3 py-2 text-sm">
            <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
            <div className="mt-3"><RecordHistory entity="TenantSetting" id={params.sellWithoutStockSettingId} /></div>
          </details>
        ) : null}
      </div>
    </FormSection>
  )
}
