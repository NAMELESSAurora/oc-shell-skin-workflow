import * as THREE from 'three';
import {plushFibers} from './plush-fibers.mjs';

// This page renders only authored bubble geometry; it never sees chat, keys or terminal pixels.
const shapes = await fetch('./bubble-shapes.json').then(r => r.json());
const role = new URLSearchParams(location.search).get('role') || 'zhuangfangyi';
const spec = shapes[role];
if (!spec) throw new Error('Unknown material role');
const renderer = new THREE.WebGLRenderer({alpha:true,antialias:true,preserveDrawingBuffer:true});
renderer.setPixelRatio(2); renderer.setSize(spec.width,spec.height);
renderer.setClearColor(0x000000,0);
renderer.outputColorSpace=THREE.SRGBColorSpace;
renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.05;
document.body.appendChild(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.OrthographicCamera(-spec.width/2,spec.width/2,spec.height/2,-spec.height/2,.1,2000);
camera.position.set(0,0,650);camera.lookAt(0,0,0);
// Exact Euclidean distance field avoids bevel self-intersections at petal notches.
// Three.js shades a convex gel height field with optical depth, Fresnel and soft studio reflections.
const w=spec.width*2,h=spec.height*2,mask=document.createElement('canvas');mask.width=w;mask.height=h;
const ctx=mask.getContext('2d');ctx.scale(2,2);ctx.fill(new Path2D(spec.path));
const pixels=ctx.getImageData(0,0,w,h).data;
function edt1(f,n){const out=new Float64Array(n),v=new Int32Array(n),z=new Float64Array(n+1);let k=0;z[0]=-Infinity;z[1]=Infinity;for(let q=1;q<n;q++){let s=((f[q]+q*q)-(f[v[k]]+v[k]*v[k]))/(2*(q-v[k]));while(s<=z[k]){k--;s=((f[q]+q*q)-(f[v[k]]+v[k]*v[k]))/(2*(q-v[k]));}k++;v[k]=q;z[k]=s;z[k+1]=Infinity;}k=0;for(let q=0;q<n;q++){while(z[k+1]<q)k++;out[q]=(q-v[k])**2+f[v[k]];}return out;}
function distance(toInside){const a=new Float64Array(w*h),f=new Float64Array(Math.max(w,h));for(let i=0;i<a.length;i++)a[i]=(pixels[i*4+3]>127)===toInside?0:1e9;for(let y=0;y<h;y++){for(let x=0;x<w;x++)f[x]=a[y*w+x];const d=edt1(f,w);for(let x=0;x<w;x++)a[y*w+x]=d[x];}for(let x=0;x<w;x++){for(let y=0;y<h;y++)f[y]=a[y*w+x];const d=edt1(f,h);for(let y=0;y<h;y++)a[y*w+x]=d[y];}return a;}
const outside=distance(false),inside=distance(true),sdf=new Float32Array(w*h);
for(let i=0;i<sdf.length;i++)sdf[i]=(pixels[i*4+3]>127?Math.sqrt(outside[i])-.5:-Math.sqrt(inside[i])+.5)/2;
function soften(input){const tmp=new Float32Array(w*h),out=new Float32Array(w*h),weights=Array.from({length:25},(_,i)=>Math.exp(-((i-12)**2)/32));const total=weights.reduce((a,b)=>a+b,0);for(let y=0;y<h;y++)for(let x=0;x<w;x++){let v=0;for(let k=-12;k<=12;k++)v+=input[y*w+Math.max(0,Math.min(w-1,x+k))]*weights[k+12];tmp[y*w+x]=v/total;}for(let y=0;y<h;y++)for(let x=0;x<w;x++){let v=0;for(let k=-12;k<=12;k++)v+=tmp[Math.max(0,Math.min(h-1,y+k))*w+x]*weights[k+12];out[y*w+x]=v/total;}return out;}
function texture(values){const t=new THREE.DataTexture(values,w,h,THREE.RedFormat,THREE.FloatType);t.minFilter=t.magFilter=THREE.LinearFilter;t.needsUpdate=true;return t;}
const field=texture(soften(sdf)),boundary=texture(sdf);
const fiberTexture=plushFibers(spec,sdf,w);
const gel=new THREE.ShaderMaterial({transparent:true,depthWrite:false,uniforms:{field:{value:field},boundary:{value:boundary},fibers:{value:fiberTexture},texel:{value:new THREE.Vector2(1/w,1/h)},tint:{value:new THREE.Color(spec.color)},plush:{value:spec.surface==='snow-plush'?1:0},size:{value:new THREE.Vector2(spec.width,spec.height)}},
  vertexShader:`varying vec2 uvGel;void main(){uvGel=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}`,
  fragmentShader:`uniform sampler2D field;uniform sampler2D boundary;uniform sampler2D fibers;uniform vec2 texel;uniform vec3 tint;uniform float plush;uniform vec2 size;varying vec2 uvGel;
  float distanceAt(vec2 p){return texture2D(field,vec2(p.x,1.0-p.y)).r;}
  float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
  float noise(vec2 p){vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y);}
  void main(){float d=distanceAt(uvGel);float raw=texture2D(boundary,vec2(uvGel.x,1.0-uvGel.y)).r;float coverage=smoothstep(-0.55,0.65,raw);
    if(plush>0.5){vec2 px=uvGel*size;float fur=noise(px*1.9);float hairs=noise(vec2(px.x*.75+px.y*.2,px.y*2.8));float furLength=3.0+noise(px*.14)*3.0;coverage=smoothstep(-furLength,1.4,raw+fur*2.5)*exp(-max(-raw,0.0)/3.0);vec4 strand=texture2D(fibers,uvGel);if(coverage+strand.a<0.001)discard;
      vec2 xy=(uvGel-vec2(.5,.52))*vec2(1.9,1.82);float z=sqrt(max(.03,1.0-dot(xy,xy)));vec3 n=normalize(vec3(xy,z));float lit=max(0.0,dot(n,normalize(vec3(-.48,.65,1.0))));float flank=pow(1.0-z,1.3);float fine=(hairs-.5)*.02+(fur-.5)*.014;vec3 color=mix(tint,tint*vec3(.68,.78,.88),flank*.36)*(0.77+lit*.25)+vec3(fine);color+=vec3(.024)*exp(-length(xy-vec2(-.28,.38))*3.0);float alpha=coverage*.97;float finalAlpha=alpha+strand.a*(1.0-alpha);color=mix(color,strand.rgb,strand.a/max(.001,finalAlpha));gl_FragColor=vec4(color,finalAlpha);
      #include <tonemapping_fragment>
      #include <colorspace_fragment>
      return;
    }
    if(coverage<0.001)discard;
    float dd=max(0.1,d);vec2 gradient=vec2(distanceAt(uvGel+vec2(texel.x*4.0,0.0))-distanceAt(uvGel-vec2(texel.x*4.0,0.0)),distanceAt(uvGel+vec2(0.0,texel.y*4.0))-distanceAt(uvGel-vec2(0.0,texel.y*4.0)))/4.0;
    float e=exp(-dd/18.0);float slope=min(5.0,0.55*e/sqrt(max(0.005,1.0-e)))*(1.0-smoothstep(28.0,52.0,dd));
    vec3 n=normalize(vec3(-gradient*slope+(uvGel-0.5)*0.28,1.0)),view=vec3(0.0,0.0,1.0),light=normalize(vec3(-0.55,0.65,1.0));
    float fresnel=0.020+0.98*pow(1.0-max(0.0,dot(n,view)),2.0);
    float shine=pow(max(0.0,dot(n,normalize(light+view))),105.0);
    float softbox=exp(-pow((uvGel.x+.55*uvGel.y-.61)/.075,2.0))*exp(-pow((uvGel.y-.77)/.16,2.0));
    float rim=exp(-dd/8.5),shoulder=exp(-pow((dd-12.0)/6.0,2.0));
    float lit=max(0.0,dot(n,light));float thickness=25.0*sqrt(max(0.0,1.0-exp(-dd/22.0)));
    vec3 absorption=exp(-(vec3(1.0)-tint)*thickness*0.021);
    vec3 color=tint*absorption*(0.86+0.18*lit)+vec3(0.19)*rim*lit+vec3(0.92)*shine+vec3(0.12)*fresnel+vec3(.40)*softbox;
    color=mix(color,vec3(1.0),shoulder*shine*0.22);
    float alpha=clamp(0.23+exp(-dd/33.0)*.34+rim*.14+fresnel*.20+shine*.19+softbox*.13,0.0,0.9)*coverage;
    gl_FragColor=vec4(color,alpha);
    #include <tonemapping_fragment>
    #include <colorspace_fragment>
  }`});
scene.add(new THREE.Mesh(new THREE.PlaneGeometry(spec.width,spec.height),gel));
renderer.render(scene,camera);
window.materialReady=true;
window.exportMaterial=()=>({role,revision:THREE.REVISION,width:renderer.domElement.width,height:renderer.domElement.height,png:renderer.domElement.toDataURL('image/png'),parameters:{surface:spec.surface==='snow-plush'?'fluffy snowball with fine fibers':'distance-field convex gel',ior:spec.surface==='snow-plush'?null:1.33,thickness:25,lighting:spec.surface==='snow-plush'?'diffuse icy rim, warm white fibers':'Fresnel, optical absorption, soft studio reflection'}});
