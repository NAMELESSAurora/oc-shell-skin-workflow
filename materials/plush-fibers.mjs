import * as THREE from 'three';

// Deterministic, curved micro-fibers, authored as material detail rather than character art.
export function plushFibers(spec, distance, fieldWidth) {
  const canvas=document.createElement('canvas');canvas.width=spec.width*2;canvas.height=spec.height*2;
  const ctx=canvas.getContext('2d');ctx.scale(2,2);ctx.lineCap='round';
  let seed=spec.kind.length*137+13;
  function random(){seed=(Math.imul(seed,1664525)+1013904223)|0;return (seed>>>0)/4294967296;}
  const strands=[];
  for(let y=4;y<spec.height-4;y+=3.8)for(let x=4;x<spec.width-4;x+=3.8){
    const px=x+(random()-.5)*4,py=y+(random()-.5)*4;
    const d=distance[Math.floor(py*2)*fieldWidth+Math.floor(px*2)];if(d<0)continue;
    let angle=.55+Math.sin(px*.018)*.55+(random()-.5)*1.8;
    let length=3+random()*5.5;
    if(d<5){angle=Math.atan2(py-spec.height/2,px-spec.width/2)+(random()-.5)*.8;length=3+random()*5;}
    const dx=Math.cos(angle)*length,dy=Math.sin(angle)*length;
    strands.push([px,py,dx,dy,(random()-.5)*2]);
  }
  function trace(s,ox=0,oy=0){ctx.beginPath();ctx.moveTo(s[0]+ox,s[1]+oy);ctx.quadraticCurveTo(s[0]+s[2]*.45-s[3]*.18+s[4]+ox,s[1]+s[3]*.4+s[2]*.15+oy,s[0]+s[2]+ox,s[1]+s[3]+oy);ctx.stroke();}
  // The offset underside gives each fiber a soft light/shadow pair without grainy speckles.
  ctx.strokeStyle='rgba(82,106,130,.11)';ctx.lineWidth=.65;for(const s of strands)trace(s,.24,.34);
  ctx.strokeStyle='rgba(255,255,255,.32)';ctx.lineWidth=.45;for(const s of strands)trace(s);
  const texture=new THREE.CanvasTexture(canvas);texture.minFilter=texture.magFilter=THREE.LinearFilter;texture.colorSpace=THREE.SRGBColorSpace;
  return texture;
}
