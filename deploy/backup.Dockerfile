# Backup diário cifrado (FND-10 CA 11 a 14): pg_dump por banco -> age -> rclone (S3 compatível), tudo em stream.
FROM alpine:3.22
RUN apk add --no-cache postgresql17-client age rclone ca-certificates tzdata \
    && adduser -D -u 10001 backup
COPY --chmod=0555 deploy/backup/backup.sh /usr/local/bin/backup.sh
USER 10001:10001
ENTRYPOINT ["/usr/local/bin/backup.sh"]
CMD ["daily"]
