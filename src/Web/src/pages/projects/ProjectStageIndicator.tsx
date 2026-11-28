import { StageIndicator } from '@/components/ui/stage-indicator'
import { StatusChip } from '@/components/ui/status-chip'
import { stageView } from '../contracts/contracts'

type StageSource = Parameters<typeof stageView>[0]

/**
 * Etapa do projeto no display de andar pelo tipo (EXE-01b CA 5, 12 e 13): fabricação P E F I T C, serviço P · I · E · A · C com o "A" em traço
 * quando o aceite é dispensado; cancelado vira chip neutro. Na fabricação, o mesmo de sempre.
 */
export function ProjectStageIndicator({ project, showLabel }: { project: StageSource; showLabel?: boolean }) {
  const s = stageView(project)
  if (s.index < 0) return <StatusChip tone="neutral">Cancelado</StatusChip>
  return <StageIndicator stages={s.stages} letters={s.letters} current={s.index} waived={s.waived} showLabel={showLabel} />
}
