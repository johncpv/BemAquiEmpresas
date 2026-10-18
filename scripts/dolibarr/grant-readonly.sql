-- Gerado de SnapshotTables (MIG-01a, ADR 0017). Não edite à mão: rode o SnapshotScriptsTests com BQ_UPDATE_SNAPSHOT_SQL=1.
-- Uso (runbook docs/operacao/migracao-dolibarr.md): mariadb <banco-do-dolibarr> < grant-readonly.sql
-- O usuário 'bqe_migracao_ro'@'localhost' é criado antes, com a senha do arquivo de secret; aqui só entram as permissões.
GRANT SELECT (`name`, `value`, `entity`) ON `llx_const` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `nom`, `name_alias`, `client`, `fournisseur`, `siren`, `siret`, `ape`, `idprof4`, `tva_intra`, `address`, `zip`, `town`, `fk_departement`, `fk_pays`, `phone`, `email`, `note_private`, `note_public`, `status`, `cond_reglement`, `fk_typent`, `entity`, `tms`) ON `llx_societe` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `fk_soc`, `lastname`, `firstname`, `poste`, `email`, `phone`, `phone_mobile`, `address`, `zip`, `town`, `statut`, `entity`, `tms`) ON `llx_socpeople` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `code`, `libelle`, `nbjour`, `type_cdr`, `decalage`, `active`, `entity`) ON `llx_c_payment_term` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `ref`, `label`, `bank`, `code_banque`, `code_guichet`, `number`, `cle_rib`, `courant`, `clos`, `entity`) ON `llx_bank_account` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `code_departement`) ON `llx_c_departements` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`id`, `code`) ON `llx_c_typent` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `label`, `type`, `fk_parent`, `entity`) ON `llx_categorie` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`fk_categorie`, `fk_soc`) ON `llx_categorie_fournisseur` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`fk_categorie`, `fk_product`) ON `llx_categorie_product` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `code`, `label`, `active`) ON `llx_c_units` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `ref`, `label`, `fk_product_type`, `fk_unit`, `customcode`, `fk_country`, `seuil_stock_alerte`, `tobatch`, `tosell`, `tobuy`, `entity`, `tms`) ON `llx_product` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`fk_object`, `elev_garantia`) ON `llx_product_extrafields` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `fk_product`, `fk_soc`, `ref_fourn`, `quantity`, `fk_unit`, `entity`) ON `llx_product_fournisseur_price` TO 'bqe_migracao_ro'@'localhost';
GRANT SELECT (`rowid`, `ref`, `label`, `lieu`, `statut`, `fk_parent`, `entity`) ON `llx_entrepot` TO 'bqe_migracao_ro'@'localhost';
