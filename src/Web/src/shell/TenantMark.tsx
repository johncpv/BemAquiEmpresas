/** Marca do tenant (CAD-06 CA 5): o logo publicado, ou a inicial do nome enquanto não houver logo. */
export function TenantMark({ name, logoUrl }: { name: string; logoUrl?: string | null }) {
  if (logoUrl) {
    return <img src={logoUrl} alt="" aria-hidden="true" className="size-[34px] rounded-[8px] bg-card object-contain" />
  }

  return (
    <span aria-hidden="true" className="grid size-[34px] place-items-center rounded-[8px] bg-signal font-display text-[15px] font-extrabold text-signal-foreground font-widest">
      {name.charAt(0).toUpperCase()}
    </span>
  )
}
