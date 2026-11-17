#!/usr/bin/env python3
"""Create fresh PRIVATE local deployment inputs. Never overwrite secrets for existing volumes."""
import argparse, json, os, secrets, subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--directory',default='.local/deployment');p.add_argument('--project',default='orderflow-deployment');p.add_argument('--port',type=int,default=8443);p.add_argument('--subnet',default='172.29.240.0/24');p.add_argument('--proxy-ip',default='172.29.240.10');args=p.parse_args()
if not args.project or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789-' for c in args.project):p.error('Use a lowercase project identifier.')
root=Path(args.directory).resolve()
repository=Path(__file__).resolve().parents[2]
if root.is_relative_to(repository) and not root.is_relative_to(repository/'.local'):
 p.error('Keep repository secrets under ignored .local, or outside the checkout/build context.')
root.mkdir(mode=0o700,parents=True,exist_ok=False)
def write(name,value):
 path=root/name;path.write_text(value);path.chmod(0o444);return path
def config(name,value):return write(name,json.dumps(value,indent=2)+'\n')
passwords={name:secrets.token_hex(32) for name in ['postgres','migrator','api','worker','simulator','rabbitmq','redis']}
for name,value in passwords.items():write(name+'_password',value+'\n')
wrap=secrets.token_hex(32);write('wrapping_password',wrap)
for prefix in ['tls','wrapping']:
 subprocess.run(['openssl','req','-x509','-newkey','rsa:3072','-nodes','-days','365','-subj','/CN=localhost' if prefix=='tls' else '/CN=OrderFlow Local Wrapping','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1','-keyout',str(root/(prefix+'.key')),'-out',str(root/(prefix+'.crt'))],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 for suffix in ['key','crt']:(root/(prefix+'.'+suffix)).chmod(0o444)
subprocess.run(['openssl','pkcs12','-export','-inkey',str(root/'wrapping.key'),'-in',str(root/'wrapping.crt'),'-out',str(root/'wrapping.pfx'),'-passout','file:'+str(root/'wrapping_password')],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
(root/'wrapping.pfx').chmod(0o444)
def connection(role,db='orderflow'):return f'Host=postgres;Database={db};Username=orderflow_{role};Password={passwords[role]};Timeout=5;GSS Encryption Mode=Disable'
api={'ConnectionStrings':{'OrderFlow':connection('api')},'RabbitMq':{'Password':passwords['rabbitmq']},'Deployment':{'CertificatePassword':wrap},'Realtime':{'RedisConnectionString':'redis:6379,password='+passwords['redis']},'CatalogCache':{'RedisConnectionString':'redis:6379,password='+passwords['redis']}}
worker={'ConnectionStrings':{'OrderFlow':connection('worker')},'RabbitMq':{'Password':passwords['rabbitmq']}}
config('api.json',api);config('worker.json',worker);config('migrator.json',{'ConnectionStrings':{'OrderFlow':connection('migrator')}})
config('bootstrap.json',{'ConnectionStrings':{'OrderFlow':connection('migrator')},'DevelopmentSeed':{'Account':{'Enabled':True,'Email':'admin@example.invalid','Password':'Aa1!'+secrets.token_hex(24),'Role':'Admin'}}})
keys={name:secrets.token_hex(32) for name in ['ApiKey','ControlKey','CallbackSigningKey']}
config('simulator.json',{'ConnectionStrings':{'OrderFlow':connection('simulator','orderflow_simulator')},'Simulator':keys})
config('api-demo.json',{**api,'PaymentCallbacks':{'Secret':keys['CallbackSigningKey']}})
config('worker-demo.json',{**worker,'PaymentGateway':{'ApiKey':keys['ApiKey']}})
write('redis.conf','bind 0.0.0.0\nprotected-mode yes\nappendonly yes\ndir /data\nrequirepass '+passwords['redis']+'\n')
write('deployment.env',f'COMPOSE_PROJECT_NAME={args.project}\nORDERFLOW_DEPLOY_CONFIG={root}\nORDERFLOW_DEPLOY_PORT={args.port}\nORDERFLOW_DEPLOY_SUBNET={args.subnet}\nORDERFLOW_PROXY_IP={args.proxy_ip}\nORDERFLOW_PUBLIC_HOST=localhost\n')
print('Private deployment inputs created at '+str(root)+'. Reuse this directory with its volumes; credentials were not printed.')
