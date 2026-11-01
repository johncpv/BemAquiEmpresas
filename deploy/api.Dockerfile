# API e migrator (mesma imagem; o migrator roda com o comando "migrate"). Contexto: raiz do repositório.
# A mesma imagem leva o worker fiscal-pdf-reader (FIS-05, ADR 0043) em /app/pdf-reader: o serviço do compose troca o entrypoint.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Host/BemAqui.Host/BemAqui.Host.csproj -c Release -o /app --nologo \
 && dotnet publish src/Workers/BemAqui.PdfReader/BemAqui.PdfReader.csproj -c Release -o /pdf-reader --nologo

# Alpine com ICU (normalização de texto e cultura pt-BR) e tzdata (America/Sao_Paulo). Usuário não-root "app" (uid 1654).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs icu-data-full tzdata krb5-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=UTC
# O volume de anexos herda o dono deste diretório na primeira montagem.
RUN mkdir -p /data/files && chown 1654:1654 /data/files
# Socket do worker fiscal-pdf-reader (FIS-05): o volume herda este dono (o worker, uid 1655) e o grupo da API (1654), sem acesso de outros.
RUN mkdir -p /run/pdf-reader && chown 1655:1654 /run/pdf-reader && chmod 0770 /run/pdf-reader
WORKDIR /app
COPY --from=build /app .
COPY --from=build /pdf-reader ./pdf-reader
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 CMD wget -qO /dev/null http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "BemAqui.Host.dll"]
