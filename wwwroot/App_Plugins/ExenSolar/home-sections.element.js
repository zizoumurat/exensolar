import { LitElement, html, css, property, state, customElement } from "@umbraco-cms/backoffice/external/lit";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_CONTENT_WORKSPACE_CONTEXT } from "@umbraco-cms/backoffice/content";

class ExenHomeSectionsElement extends UmbLitElement {
  static properties = {
    value: { type: String },
    readonly: { type: Boolean },
    _data: { state: true },
    _dialog: { state: true },
    _draft: { state: true },
    _imageValue: { state: true }
    ,_isHome: { state: true }
  };

  static styles = css`
    :host{display:block;color:var(--uui-color-text)} .section{border:1px solid var(--uui-color-border);border-radius:8px;margin:0 0 24px;overflow:hidden;background:var(--uui-color-surface)}
    .head{display:flex;align-items:center;justify-content:space-between;padding:16px 20px;border-bottom:1px solid var(--uui-color-border)} h3{font-size:18px;margin:0;font-weight:600}
    button{font:inherit;cursor:pointer;border:0;border-radius:5px;padding:9px 14px;background:var(--uui-color-positive);color:white} button.secondary{background:var(--uui-color-surface-alt);color:var(--uui-color-text)} button.danger{background:var(--uui-color-danger);padding:7px 10px}
    table{width:100%;border-collapse:collapse} th,td{text-align:left;padding:12px 16px;border-bottom:1px solid var(--uui-color-border);vertical-align:middle} th{font-weight:600;color:var(--uui-color-text-alt)} tr:last-child td{border-bottom:0}.thumb{width:92px;height:58px;object-fit:cover;border-radius:4px;background:#eee}.empty{padding:22px;color:var(--uui-color-text-alt)} .actions{display:flex;gap:8px;justify-content:flex-end}
    .overlay{position:fixed;inset:0;background:#0009;z-index:1000;display:grid;place-items:center;padding:20px}.dialog{background:var(--uui-color-surface);border-radius:10px;width:min(680px,100%);max-height:90vh;overflow:auto;box-shadow:0 12px 48px #0005}.dialog-head,.dialog-foot{padding:16px 22px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid var(--uui-color-border)}.dialog-foot{border:0;border-top:1px solid var(--uui-color-border);justify-content:flex-end;gap:10px}.dialog-body{padding:20px 22px}.dialog h3{font-size:20px}.field{display:grid;gap:7px;margin-bottom:18px}.field label{font-weight:600}.field input[type=text],.field textarea{font:inherit;padding:10px;border:1px solid var(--uui-color-border);border-radius:5px;background:var(--uui-color-surface);color:var(--uui-color-text)}textarea{min-height:100px;resize:vertical}.radio{display:flex;gap:18px;align-items:center}.radio label{font-weight:400;display:flex;gap:7px;align-items:center}
  `;

  constructor(){super();this.value="";this.readonly=false;this._data={slides:[],partners:[]};this._dialog="";this._draft={};this._imageValue=[];this._isHome=false;this.consumeContext(UMB_CONTENT_WORKSPACE_CONTEXT,context=>{if(context)this.observe(context.name(),name=>{this._isHome=name==="Ana Sayfa";this.requestUpdate();},null)});}
  willUpdate(changed){if(changed.has("value")){try{this._data={slides:[],partners:[],...JSON.parse(this.value||"{}")} }catch{this._data={slides:[],partners:[]}}}}
  _setInput(key,e){this._draft={...this._draft,[key]:e.target.value};}
  _open(kind,item){this._dialog=kind;this._draft=item?{...item}:{id:crypto.randomUUID(),title:"",description:"",image:"",link:"",linkText:"Teklif Al",showQuote:true};this._imageValue=this._draft.image? [{key:crypto.randomUUID(),mediaKey:this._draft.image,mediaTypeAlias:"",crops:[],focalPoint:null}]:[];}
  _imageChanged(e){const value=e.target.value??e.detail?.value??[];this._imageValue=value;this._draft={...this._draft,image:value[0]?.mediaKey??""};}
  _save(){const key=this._dialog==="slides"?"slides":"partners", rows=[...(this._data[key]??[])];const i=rows.findIndex(x=>x.id===this._draft.id);if(i<0)rows.push(this._draft);else rows[i]=this._draft;this._data={...this._data,[key]:rows};this.value=JSON.stringify(this._data);this.dispatchEvent(new UmbChangeEvent());this._dialog="";}
  _remove(key,item){if(!confirm(`“${item.title}” kaydı silinsin mi?`))return;this._data={...this._data,[key]:this._data[key].filter(x=>x.id!==item.id)};this.value=JSON.stringify(this._data);this.dispatchEvent(new UmbChangeEvent());}
  _table(kind,label){const rows=this._data[kind]??[];return html`<section class="section"><div class="head"><h3>${label}</h3><button ?disabled=${this.readonly} @click=${()=>this._open(kind)}>＋ Yeni Kayıt</button></div>${rows.length?html`<table><thead><tr><th>Görsel</th><th>Başlık</th>${kind==="slides"?html`<th>Teklif Al</th>`:""}<th></th></tr></thead><tbody>${rows.map(item=>html`<tr><td>${item.image?html`<umb-input-rich-media .value=${[{key:"table-image",mediaKey:item.image,mediaTypeAlias:"",crops:[],focalPoint:null}]} max="1" min="0" readonly></umb-input-rich-media>`:"—"}</td><td>${item.title||"Başlıksız"}</td>${kind==="slides"?html`<td>${item.showQuote?"Göster":"Gizle"}</td>`:""}<td><div class="actions"><button class="secondary" ?disabled=${this.readonly} @click=${()=>this._open(kind,item)}>Düzenle</button><button class="danger" ?disabled=${this.readonly} @click=${()=>this._remove(kind,item)}>Sil</button></div></td></tr>`)}</tbody></table>`:html`<div class="empty">Henüz kayıt eklenmemiş.</div>`}</section>`;}
  render(){if(!this._isHome){this.hidden=true;return html``;}this.hidden=false;return html`${this._table("slides","Kayan Resimler")}${this._table("partners","Çözüm Ortaklarımız")}${this._dialog?html`<div class="overlay" @click=${e=>{if(e.target===e.currentTarget)this._dialog=""}}><section class="dialog" role="dialog" aria-modal="true"><div class="dialog-head"><h3>${this._draft.id&&((this._data[this._dialog]??[]).some(x=>x.id===this._draft.id))?"Kaydı Düzenle":"Yeni Kayıt"}</h3><button class="secondary" @click=${()=>this._dialog=""}>✕ Kapat</button></div><div class="dialog-body"><div class="field"><label>Başlık</label><input type="text" .value=${this._draft.title??""} @input=${e=>this._setInput("title",e)}></div>${this._dialog==="slides"?html`<div class="field"><label>Açıklama</label><textarea .value=${this._draft.description??""} @input=${e=>this._setInput("description",e)}></textarea></div>`:""}<div class="field"><label>Görsel yükleme</label><umb-input-rich-media .value=${this._imageValue} .max=${1} .min=${0} @change=${e=>this._imageChanged(e)}></umb-input-rich-media></div>${this._dialog==="slides"?html`<div class="field"><label>Teklif Al butonu</label><div class="radio"><label><input type="radio" name="showQuote" .checked=${this._draft.showQuote!==false} @change=${()=>this._draft={...this._draft,showQuote:true}}> Göster</label><label><input type="radio" name="showQuote" .checked=${this._draft.showQuote===false} @change=${()=>this._draft={...this._draft,showQuote:false}}> Gizle</label></div></div>`:""}</div><div class="dialog-foot"><button class="secondary" @click=${()=>this._dialog=""}>Vazgeç</button><button @click=${()=>this._save()}>Kaydet</button></div></section></div>`:""}`;}
}
customElements.define("exen-home-sections",ExenHomeSectionsElement);
export { ExenHomeSectionsElement };
