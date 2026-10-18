export const TENANT_STATUS = { Active: ['ok', 'Ativo'], Provisioning: ['info', 'Provisionando'], Suspended: ['crit', 'Suspenso'] } as const

export type TenantStatusName = keyof typeof TENANT_STATUS

/** Nomes das etapas do provisionamento (FND-03a) para a tela. */
export const STEP_LABEL: Record<string, string> = {
  Register: 'Registro',
  CreateRole: 'Usuário do banco',
  CreateDatabase: 'Banco',
  Migrate: 'Estrutura',
  Seed: 'Dados iniciais',
  CreateOwner: 'Proprietário',
  SendInvite: 'Convite',
  Activate: 'Ativação',
}
