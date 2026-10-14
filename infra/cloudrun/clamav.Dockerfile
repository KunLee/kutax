# ClamAV for the Cloud Run sidecar. Signatures are downloaded when the image is built, so an instance that starts from
# zero only loads them from disk instead of fetching ~200 MB first; freshclam keeps them current while it runs.
# Rebuild (scripts/Deploy-CloudRun.ps1) at least weekly so a cold start never begins with old signatures.
FROM clamav/clamav:1.5.4@sha256:7659dcb0db47941d3cf8336af84bbb63c7e70b76fc00601774358412b42ed186
# One copy of the signatures in memory: reloading alongside the old set would double clamd's memory for a minute.
RUN sed -i 's/^#\?ConcurrentDatabaseReload .*/ConcurrentDatabaseReload no/' /etc/clamav/clamd.conf \
    && grep -q '^ConcurrentDatabaseReload no' /etc/clamav/clamd.conf || echo 'ConcurrentDatabaseReload no' >> /etc/clamav/clamd.conf
RUN freshclam --foreground --stdout && chown -R clamav:clamav /var/lib/clamav
