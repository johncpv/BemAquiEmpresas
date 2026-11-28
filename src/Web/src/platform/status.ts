export const TENANT_STATUS = { Active: ['ok', 'Ativo'], Provisioning: ['info', 'Provisionando'], Suspended: ['crit', 'Suspenso'] } as const

export type TenantStatusName = keyof typeof TENANT_STATUS

/** Nomes das etapas do provisionamento (FND-03a) para a tela. */
export const STEP_LABEL: Record<string, string> = {
  Register: 'Registro',
  Profile: 'Perfil de negócio',
  CreateRole: 'Usuário do banco',
  CreateDatabase: 'Banco',
  Migrate: 'Estrutura',
  Seed: 'Dados iniciais',
  NewTenantDefaults: 'Padrões de tenant novo',
  CreateOwner: 'Proprietário',
  SendInvite: 'Convite',
  Activate: 'Ativação',
}
