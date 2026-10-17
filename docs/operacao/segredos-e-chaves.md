# Runbook: segredos e chaves (Data Protection)

Implementa a ADR 0007 (FND-11). Este runbook serve para quem opera a VPS. Ele explica três coisas: o que precisa ser guardado, como restaurar e como trocar o certificado.

## O que existe

| Peça | Onde fica | Para que serve |
|---|---|---|
| **Key ring** | Banco de catálogo, tabela `security.data_protection_keys` | Chaves que cifram todos os segredos: conexão de cada tenant, SMTP, TOTP, chave de IA e, mais tarde, Inter e certificado fiscal |
| **Certificado de proteção** (`.pfx`) | Secret do Docker montado em `/run/secrets/dp-cert.pfx` | Cifra cada chave do key ring. Sem ele, o key ring não serve para nada |
| **Senha do `.pfx`** | Outro secret, configurado em `DataProtection__CertificatePassword` | Abre o `.pfx` |
| **Cópia offline** | Cofre de senhas do PO, fora da VPS | Única forma de recuperar tudo se a VPS se perder |

Algumas regras decorrem disso:
- Um backup do banco sozinho não decifra nada, porque falta o certificado.
- O certificado sozinho também não decifra nada, porque falta o key ring.
- Perder os dois ao mesmo tempo significa perder todos os segredos de todos os tenants.

## Configuração do Host

```
ConnectionStrings__Catalog=...
DataProtection__CertificatePath=/run/secrets/dp-cert.pfx
DataProtection__CertificatePassword=<secret>
DataProtection__AllowKeyRingCreation=false   # true só no primeiro provisionamento
DataProtection__AutoMigrate=false            # em homolog e prod, quem migra é o migrator (ADR 0006)
```

O Host **recusa subir** e registra o motivo no log nestes casos:
- o certificado não está configurado, ou o arquivo não existe;
- o key ring está inacessível (banco fora do ar ou tabela ausente);
- o key ring está vazio e `AllowKeyRingCreation=false`. Isso acontece, por exemplo, ao restaurar um backup sem as chaves;
- alguma chave não decifra com o certificado configurado (certificado errado).

Em desenvolvimento, sem certificado configurado, o Host gera um certificado autoassinado em `src/Host/BemAqui.Host/.local/`, que fica fora do git.

## Primeiro provisionamento (uma vez por ambiente)

1. **Gere o certificado.** Use RSA 3072 com validade de 10 anos. Na estação do PO, você tem duas opções:
   - com OpenSSL:
     ```bash
     openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
       -subj "/CN=BemAqui Empresas DataProtection (prod)" -keyout dp.key -out dp.crt
     openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp-cert.pfx   # define a senha
     shred -u dp.key
     ```
   - em C#, com `ProtectionCertificate.CreateSelfSigned(subject, senha)`, que gera o mesmo formato.
2. **Guarde a cópia offline.** O `dp-cert.pfx` e a senha vão para o cofre de senhas do PO, como item "BemAqui Empresas: certificado Data Protection (ambiente)". Nunca coloque essa cópia no repositório, no banco ou no bucket de backup dos bancos.
3. **Instale na VPS.** Copie o `.pfx` para `/opt/projetos/bemaqui-empresas[-homolog]/secrets/dp-cert.pfx`, com `chmod 0400` e dono igual ao uid do container. Monte o arquivo como secret no compose.
4. **Crie o key ring.** Suba o Host **uma vez** com `DataProtection__AllowKeyRingCreation=true`. Confira no log a mensagem "Key ring do Data Protection pronto".
5. **Desligue a flag.** Volte `AllowKeyRingCreation` para `false` e reinicie.

## Backup do key ring (trilha separada)

- O dump diário do catálogo já inclui `security.data_protection_keys`, que está cifrado.
- Além disso, há uma **trilha separada**: um export semanal só do key ring, com outro destino e outra credencial, diferentes do backup dos bancos:
  ```bash
  pg_dump -h <host> -U <user> -d catalog -t security.data_protection_keys --data-only -Fc -f keyring-$(date +%F).dump
  age -r <chave-publica-do-PO> keyring-*.dump > keyring-$(date +%F).dump.age   # depois envie ao destino da trilha separada
  ```
- O destino exato da trilha separada (bucket e provedor) é definido na FND-10b. Até lá, a cópia offline do PO cobre a recuperação.

## Restauração (outra máquina ou VPS nova)

1. Restaure o catálogo e os bancos de tenant a partir dos dumps.
2. Garanta que `security.data_protection_keys` tem linhas. Se não tiver, restaure a trilha separada do key ring.
3. Monte o `.pfx` da cópia offline e configure a senha.
4. Suba com `AllowKeyRingCreation=false`. Se o Host subir, os segredos decifram. Se recusar, a mensagem no log diz qual peça falta.
5. **Nunca** suba com `AllowKeyRingCreation=true` para "destravar" uma restauração. Isso cria um key ring novo, e os segredos antigos continuam ilegíveis.

Teste mensal: restaure numa máquina limpa e confira que um segredo decifra. O teste automatizado equivalente é `DataProtectionKeyRingTests.Restauracao_com_key_ring_decifra_e_sem_key_ring_nao`.

## Rotação

- **Chaves:** o Data Protection cria uma chave nova a cada 90 dias. As antigas continuam decifrando, e os segredos regravados passam a usar a chave nova. Não há nada a fazer.
- **Troca do certificado** (vencimento ou suspeita de vazamento):
  1. Gere o novo `.pfx` e guarde a cópia offline.
  2. Configure os dois: o novo como `CertificatePath` e o antigo na lista de decifração. O Host ainda não lê essa lista de configuração; isso fica pendente até a primeira troca real, na FND-10b.
  3. Force uma chave nova. Ela já nasce cifrada pelo certificado novo.
  4. Depois da próxima rotação, revogue as chaves antigas e retire o certificado antigo.
