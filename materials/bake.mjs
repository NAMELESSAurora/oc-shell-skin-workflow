import {createServer} from 'node:http';
import {readFile, writeFile, mkdir, access} from 'node:fs/promises';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {dirname, join, extname, resolve} from 'node:path';
import {createHash} from 'node:crypto';

const root = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const options = {shapes:join(root,'bubble-shapes.json'), output:resolve(root,'../build/materials'), roles:[], replace:false};
for (let i=0;i<args.length;i++) {
  const arg=args[i];
  if (arg==='--replace') options.replace=true;
  else if (['--shapes','--output','--playwright-module'].includes(arg)) {
    if (!args[i+1]) throw new Error(`Missing value for ${arg}`);
    options[arg.slice(2).replace('-module','Module')]=resolve(args[++i]);
  } else if (arg==='--role') {
    if (!args[i+1]) throw new Error('Missing role');
    options.roles.push(args[++i]);
  } else throw new Error(`Unknown option: ${arg}`);
}
const shapes=JSON.parse(await readFile(options.shapes,'utf8'));
const selected=options.roles.length?options.roles:Object.keys(shapes);
if (!selected.length) throw new Error('No material roles');
for (const role of selected) {
  const spec=shapes[role];
  if (!/^[a-z0-9][a-z0-9_-]{0,63}$/.test(role) || !spec) throw new Error('Invalid or missing role');
  if (!Number.isInteger(spec.width)||!Number.isInteger(spec.height)||spec.width<64||spec.width>2048||spec.height<64||spec.height>2048||typeof spec.path!=='string'||!spec.path||typeof spec.kind!=='string'||!/^#[0-9a-f]{6}$/i.test(spec.color)) throw new Error(`Invalid shape: ${role}`);
}
const output=resolve(options.output);
await mkdir(output,{recursive:true});
if (!options.replace) {
  for (const filename of [...selected.map(role=>`${role}-gel.png`),'material-manifest.json']) {
    let exists=false;try{await access(join(output,filename));exists=true;}catch{}
    if(exists) throw new Error('Refusing to replace existing material output; use a new folder or --replace');
  }
}
const playwright=options.playwrightModule?await import(pathToFileURL(options.playwrightModule).href):await import('playwright');
const {chromium}=playwright.default||playwright;
const allowed=new Set(['/bubble-renderer.html','/gel-material.mjs','/plush-fibers.mjs','/vendor/three.module.js','/vendor/three.core.js']);
const server=createServer(async(req,res)=>{
  const requestPath=new URL(req.url,'http://127.0.0.1').pathname;
  if(requestPath==='/bubble-shapes.json'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify(shapes));return;}
  if(!allowed.has(requestPath)){res.writeHead(404);res.end();return;}
  try{const data=await readFile(join(root,requestPath.slice(1)));res.setHeader('Content-Type',['.js','.mjs'].includes(extname(requestPath))?'text/javascript':'text/html');res.end(data);}catch{res.writeHead(404);res.end();}
});
let browser;
try{
  await new Promise((ok,fail)=>{server.once('error',fail);server.listen(0,'127.0.0.1',ok);});
  browser=await chromium.launch({headless:true,args:['--enable-webgl','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const page=await browser.newPage();const errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  const manifest={engine:'Three.js r185',mode:'baked RGBA; native spring mesh supplies live interaction',roles:[]};
  for(const role of selected){
    await page.goto(`http://127.0.0.1:${server.address().port}/bubble-renderer.html?role=${encodeURIComponent(role)}`);
    await page.waitForFunction(()=>window.materialReady===true,{timeout:60000});
    const {png,...entry}=await page.evaluate(()=>window.exportMaterial());
    const data=Buffer.from(png.split(',')[1],'base64');
    const path=`${role}-gel.png`;await writeFile(join(output,path),data);
    manifest.roles.push({...entry,path,sha256:createHash('sha256').update(data).digest('hex').toUpperCase()});
  }
  if(errors.length)throw new Error(errors.join('\n'));
  await writeFile(join(output,'material-manifest.json'),JSON.stringify(manifest,null,2)+'\n');
  console.log(JSON.stringify({status:'passed',...manifest}));
}finally{
  if(browser)await browser.close();
  if(server.listening)await new Promise(done=>server.close(done));
}
