# Runbook: segredos e chaves (Data Protection)

Este runbook é para quem opera a VPS. Ele diz o que precisa ser guardado, como restaurar e o que **não** fazer. Implementa a ADR 0007 (FND-11).

## O que existe

| Peça | Onde fica | Para que serve |
|---|---|---|
| **Key ring** | Banco de catálogo, tabela `security.data_protection_keys` | Chaves que cifram todos os segredos: conexão de cada tenant, SMTP, TOTP, chave de IA e, mais tarde, Inter e certificado fiscal |
| **Certificado de proteção** (`.pfx`, RSA de 3072 bits ou mais, com chave privada) | Secret do Docker em `/run/secrets/dp-cert.pfx` | Cifra cada chave do key ring. Sem ele, o key ring não decifra nada |
| **Senha do `.pfx`** | Outro secret, em arquivo: `/run/secrets/dp-cert-password` (`DataProtection__CertificatePasswordFile`) | Abre o `.pfx`. Não use variável de ambiente, porque ela aparece no `docker inspect` |
| **Cópia offline** | Cofre de senhas do PO, fora da VPS | Guarda o `.pfx` e a senha. **Não** guarda o key ring |

Disso saem três consequências:
- só o backup do banco não decifra nada, porque falta o certificado;
- só o certificado também não decifra nada, porque falta o key ring;
- perder os dois ao mesmo tempo é perder todos os segredos de todos os tenants.

## Configuração do Host

```
ConnectionStrings__Catalog=...
DataProtection__CertificatePath=/run/secrets/dp-cert.pfx
DataProtection__CertificatePasswordFile=/run/secrets/dp-cert-password
DataProtection__AllowKeyRingCreation=false   # true só no primeiro provisionamento
DataProtection__AutoMigrate=false            # em homolog e prod, quem migra é o migrator (ADR 0006)
```

O Host **recusa subir** nos casos abaixo. O motivo vai para o log, pelo logger de bootstrap.
- O certificado não está configurado ou o arquivo não existe.
- A senha do certificado está errada.
- O certificado não tem chave privada, ou o RSA tem menos de 3072 bits.
- O key ring está inacessível (banco fora do ar ou tabela ausente).
- O key ring não tem nenhuma chave ativa (não revogada) e `AllowKeyRingCreation=false`. É o que acontece, por exemplo, ao restaurar um backup sem as chaves.
- Alguma chave não decifra com o certificado configurado, ou seja, o certificado está errado.

O Host registra um aviso (Warning) em duas situações:
- toda subida com `AllowKeyRingCreation=true`;
- o certificado vence em menos de 180 dias.

**Desenvolvimento local:** com `DataProtection__GenerateDevelopmentCertificate=true`, que já vem ligado no `appsettings.Development.json`, o Host gera um certificado autoassinado em `src/Host/BemAqui.Host/.local/`, fora do git. Esse opt-in é **ignorado** fora de `Development` e dentro de container (`DOTNET_RUNNING_IN_CONTAINER=true`).

## Primeiro provisionamento (uma vez por ambiente)

1. **Gere o certificado** na estação do PO, num diretório em memória (tmpfs), para a chave nunca tocar o disco:
   ```bash
   d=$(mktemp -d -p /dev/shm) && cd "$d"
   openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
     -subj "/CN=BemAqui Empresas DataProtection (prod)" \
     -addext "basicConstraints=critical,CA:FALSE" \
     -addext "keyUsage=critical,keyEncipherment,dataEncipherment" \
     -keyout dp.key -out dp.crt
   openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp-cert.pfx   # define a senha
   # copie dp-cert.pfx para o cofre e para a VPS; depois:
   cd / && rm -rf "$d"
   ```
   Alternativa: em C#, `ProtectionCertificate.CreateSelfSigned(subject, senha)` gera o mesmo formato.
2. **Guarde a cópia offline.** Coloque o `dp-cert.pfx` e a senha no cofre de senhas do PO, no item "BemAqui Empresas: certificado Data Protection (ambiente)". Nunca coloque essa cópia no repositório, no banco ou no bucket de backup dos bancos.
3. **Instale na VPS.** Copie `dp-cert.pfx` e um arquivo `dp-cert-password` para `/opt/projetos/bemaqui-empresas[-homolog]/secrets/`, com `chmod 0400` e dono = uid do container. Monte os dois como secrets no compose.
4. **Crie o key ring.** Suba **uma vez** com `DataProtection__AllowKeyRingCreation=true` e confira no log a mensagem "Key ring do Data Protection pronto".
5. **Desligue a flag.** Volte `AllowKeyRingCreation` para `false` e reinicie. Enquanto a flag estiver ligada, toda subida avisa no log.

## Backup do key ring

- O dump diário do catálogo já inclui `security.data_protection_keys`, que está cifrado.
- Há também uma **trilha separada**: um export semanal só do key ring, com outro destino e outra credencial:
  ```bash
  f=keyring-$(date +%F).dump
  pg_dump -h <host> -U <user> -d catalog -t security.data_protection_keys --data-only -Fc -f "$f"
  age -r <chave-publica-do-PO> -o "$f.age" "$f" && rm -f "$f"   # envie "$f.age" ao destino da trilha separada
  ```
- O destino da trilha separada (bucket, provedor e credencial) será definido na FND-10b, e **o CA 8 da FND-11 fica parcial até lá**. Até essa definição, o key ring só sobrevive dentro do dump do catálogo.

## Restauração (outra máquina ou VPS nova)

1. Restaure o catálogo e os bancos de tenant a partir dos dumps.
2. Confira se `security.data_protection_keys` tem linhas. Se estiver vazia, restaure a trilha separada do key ring.
3. Monte o `.pfx` e a senha a partir da cópia offline.
4. Suba com `AllowKeyRingCreation=false`.
   - Se o Host subir, os segredos decifram.
   - Se ele recusar, a mensagem no log diz qual peça falta.
5. **Nunca** suba com `AllowKeyRingCreation=true` para "destravar" uma restauração. Isso cria um key ring novo, e os segredos antigos continuam ilegíveis.

**Teste mensal:** restaure numa máquina limpa e confira que um segredo decifra. O teste automatizado equivalente é `DataProtectionKeyRingTests.Restauracao_com_key_ring_decifra_e_sem_key_ring_nao`.

## Rotação, revogação e troca do certificado

- **Chaves:** o Data Protection cria uma chave nova a cada 90 dias. As antigas continuam decifrando, e o que for regravado passa a usar a nova. Não há nada a fazer.
- **Revogação:** **NÃO revogue chaves.** O que foi cifrado com uma chave revogada deixa de decifrar, e os segredos que ainda usam essa chave (conexões de tenant, TOTP, SMTP, IA) ficam ilegíveis. Só revogue depois que existir o job de recifração (FND-10b). Esse job faz Unprotect e Protect de todos os segredos de todos os tenants e confirma que nenhum segredo ainda usa a chave antiga.
- **Troca do certificado:** **ainda não é suportada.** Hoje o Host aceita um único certificado. A lista de certificados antigos, usados só para decifrar, entra na FND-10b junto com o job de recifração. Até lá:
  - não troque nem retire o certificado em uso;
  - o certificado gerado por este runbook vale 10 anos, e a subida avisa 180 dias antes do vencimento.
- **Suspeita de vazamento do certificado:** trocar o certificado **não** protege os backups antigos, porque quem tem o certificado e um dump antigo decifra os segredos daquela época. O que fazer:
  1. Troque os segredos na origem: senhas de banco dos tenants, senha SMTP, chaves de IA e, na Fase 5, as credenciais do Inter.
  2. Quando a FND-10b existir, recifre tudo com o certificado novo.
