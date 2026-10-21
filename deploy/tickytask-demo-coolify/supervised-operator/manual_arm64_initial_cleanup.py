"""Nur vollständiges eigenes Inventar und begrenztes Cleanup des synthetischen Initialversuchs.

Keine eigenen SSH-/Dockerports. Der übergebene vertrauenswürdige Kanal ist derselbe
Source-/Root-gebundene Originaltransport; UNKNOWN-Writer sperrt jedes Cleanup.
"""
import json,re
ROLES=('ownership-pg','ownership-key','initdb','postgres','migrate','api')
VOLUMES=('postgres-data','socket','keyring')
CONTAINER_FORMAT='{"id":{{json .ID}},"name":{{json .Name}},"image":{{json .Image}},"labels":{{json .Config.Labels}},"running":{{json .State.Running}}}'
VOLUME_FORMAT='{"name":{{json .Name}},"driver":{{json .Driver}},"labels":{{json .Labels}}}'
NETWORK_FORMAT='{"id":{{json .Id}},"driver":{{json .Driver}},"internal":{{json .Internal}},"subnets":[{{range .IPAM.Config}}{{json .Subnet}},{{end}}null],"labels":{{json .Labels}},"peers":[{{range $cid,$peer := .Containers}}{{json $cid}},{{end}}null]}'
class CleanupStop(Exception):
 """Nur fester eigener Stop, niemals Ressourcen-/Rohfehlertexte."""

def require(value):
 if not value:raise CleanupStop()

def labels(scope):
 """Vollständige eigenen Runlabels, kein unsicherer Subset-/Präfixvergleich."""
 return {'com.docker.compose.project':scope['namespace'],'flowzer.tt.demo.owner':scope['owner'],
         'flowzer.tt.demo.nonce':scope['nonce'],'flowzer.tt.demo.source':scope['head'],'flowzer.tt.demo.run':scope['runId']}

def inventory(scope,containers,volumes):
 """Alle Container UND Volumes validieren, bevor auch nur ein Delete möglich ist."""
 require(type(containers) is list and len(containers)<=6 and type(volumes) is list and len(volumes)<=3)
 owned=labels(scope);ids=[];names=[];roles=[]
 for row in containers:
  require(type(row) is dict and set(row)=={'id','name','image','labels','running'} and type(row['running']) is bool)
  role=next((r for r in ROLES if row['name']=='/'+scope['namespace']+'_'+r+'_'+scope['nonce']),None)
  require(role is not None and type(row['id']) is str and re.fullmatch('[a-f0-9]{64}',row['id']))
  api=role in ('api','migrate');expected=dict(scope['apiLabels'],**owned) if api else owned
  require(row['labels']==expected and row['image']==scope['apiId' if api else 'pgId'])
  ids.append(row['id']);roles.append(role)
 require(len(set(ids))==len(ids) and len(set(roles))==len(roles))
 for row in volumes:
  require(type(row) is dict and set(row)=={'name','driver','labels'} and row['driver']=='local' and row['labels']==owned)
  require(row['name'] in [scope['namespace']+'_'+suffix for suffix in VOLUMES]);names.append(row['name'])
 require(len(set(names))==len(names));return ids,names

def read_inventory(channel,scope):
 """Nur minimal projizierte eigene Präfixmetadaten; niemals ENV/Logs/Mountpfade."""
 ids=channel.call(['ps','-a','--no-trunc','--filter','name=^/'+scope['namespace']+'_','--format','{{.ID}}'],cleanup=True,timeout=6).decode('ascii').splitlines()
 names=channel.call(['volume','ls','--filter','name=^'+scope['namespace']+'_','--format','{{.Name}}'],cleanup=True,timeout=6).decode('ascii').splitlines()
 require(len(ids)<=6 and len(set(ids))==len(ids) and all(re.fullmatch('[a-f0-9]{64}',cid) for cid in ids))
 require(len(names)<=3 and len(set(names))==len(names) and all(name in [scope['namespace']+'_'+suffix for suffix in VOLUMES] for name in names))
 containers=[json.loads(channel.call(['container','inspect',cid,'--format',CONTAINER_FORMAT],cleanup=True,timeout=6)) for cid in ids]
 volumes=[json.loads(channel.call(['volume','inspect',name,'--format',VOLUME_FORMAT],cleanup=True,timeout=6)) for name in names]
 return containers,volumes

def check_network(channel,scope,containers):
 """Originale Netzwerksemantik ohne Gatewayfeld, nur eigener laufender APIpeer."""
 network=scope['network'];value=json.loads(channel.call(['network','inspect',network['id'],'--format',NETWORK_FORMAT],cleanup=True,timeout=6))
 require(value['id']==network['id'] and value['driver']=='bridge' and value['internal'] is False
         and value['subnets']==[network['cidr'],None] and value['labels']==network['labels'])
 peers=value['peers'];require(type(peers) is list and peers[-1:]==[None])
 expected={row['id'] for row in containers if row['running'] and row['name']=='/'+scope['namespace']+'_api_'+scope['nonce']}
 require(len(peers[:-1])==len(set(peers[:-1])) and set(peers[:-1])==expected)

def cleanup(channel,scope,once):
 """Einmaliges Own-Cleanup, nie neben einem unbestätigten eigenen Writer.

 Imagecache und vorhandenes Netz bleiben unangetastet. Jeder Call verwendet die
 separate Cleanupfrist und originalen Bounds. Teilfehler bleiben UNKNOWN, ohne
 erneuten Remove/Stop oder automatisches Überschreiben des Checks.
 """
 if channel.writer_unknown or channel.own_pid is not None:return 'UNKNOWN'
 try:
  once.claim('CLEANUP')
  containers,volumes=read_inventory(channel,scope);ids,names=inventory(scope,containers,volumes)
  check_network(channel,scope,containers)
  for cid in ids:channel.call(['container','rm','--force',cid],write=True,cleanup=True,timeout=6)
  for name in names:channel.call(['volume','rm',name],write=True,cleanup=True,timeout=6)
  containers,volumes=read_inventory(channel,scope);inventory(scope,containers,volumes)
  require(not containers and not volumes);check_network(channel,scope,[])
  return 'ABSENT'
 except BaseException:return 'UNKNOWN'
